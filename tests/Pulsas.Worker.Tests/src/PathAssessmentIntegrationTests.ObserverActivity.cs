using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    private const string ObserverActivityQuery = """
        query ($observer: ID!, $from: Instant!, $to: Instant!, $filter: ObserverActivityFilterInput) {
          observerActivitySummary(observerId: $observer, from: $from, to: $to, filter: $filter) {
            __typename
            ... on ObserverActivitySummary {
              from to intervalSeconds historyCoverage receptionCount firstArrivedAt lastArrivedAt
              packetTypeCounts { packetType count }
              snr { sampleCount average } rssi { sampleCount average }
              buckets { from to receptionCount }
            }
            ... on ObserverNotFoundError { message }
          }
        }
        """;

    [PgmqFact]
    public async Task Observer_activity_counts_matching_arrivals_from_the_real_ingestion_pipeline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var sender = AdvertSigningKey.Generate();
        var edge = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 60
        );
        var from = edge.AddSeconds(-1);
        var to = edge.AddMinutes(2);
        var advert = SignedAdvert(sender, edge);
        var custom = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.RawCustom, [1, 2])
        );
        await ReceiveAndProcessAsync(database, observer, advert, from.AddTicks(-10), token);
        var first = await ReceiveAndProcessAsync(database, observer, advert, from, token);
        var second = await ReceiveAndProcessAsync(database, observer, custom, edge, token);
        await ReceiveAndProcessAsync(
            database,
            observer,
            advert,
            edge,
            token,
            observerOverride: true
        );
        await ReceiveAndProcessAsync(database, observer, advert, to, token);
        var last = edge.AddSeconds(3);
        await ReceiveAsync(database, observer, [0xff], Guid.CreateVersion7(), last, token);

        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );
        await db
            .MeshPacketReceptions.Where(row => row.Id == second)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(row => row.Signal.Snr, (double?)double.NaN)
                        .SetProperty(row => row.Signal.Rssi, (int?)null),
                token
            );
        var observerId = await db
            .MeshPacketReceptions.Where(row => row.Id == first)
            .Select(row => row.ObserverId)
            .SingleAsync(token);

        var reads = new TrafficReadInterceptor();
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        var activity = ObserverActivityResult(await ReadAsync(observerId.ToString(), null));
        Assert.Equal(3, reads.Count);
        Assert.Equal("UNKNOWN", activity.GetProperty("historyCoverage").GetString());
        Assert.Equal(from, activity.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(to, activity.GetProperty("to").GetDateTimeOffset());
        Assert.Equal(60, activity.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(3, activity.GetProperty("receptionCount").GetInt64());
        Assert.Equal(from, activity.GetProperty("firstArrivedAt").GetDateTimeOffset());
        Assert.Equal(last, activity.GetProperty("lastArrivedAt").GetDateTimeOffset());
        AssertSignal(activity, "snr", 2, 8.5);
        AssertSignal(activity, "rssi", 2, -75);
        Assert.Collection(
            activity.GetProperty("packetTypeCounts").EnumerateArray(),
            row => AssertType(row, "ADVERT"),
            row => AssertType(row, "RAW_CUSTOM"),
            row => AssertType(row, null)
        );
        Assert.Collection(
            activity.GetProperty("buckets").EnumerateArray(),
            row => AssertActivityBucket(row, from, edge, 1),
            row => AssertActivityBucket(row, edge, edge.AddMinutes(1), 2),
            row => AssertActivityBucket(row, edge.AddMinutes(1), to, 0)
        );

        var filtered = ObserverActivityResult(
            await ReadAsync(
                observerId.ToString(),
                new { regionCodes = new[] { "TST" }, packetTypes = new[] { "ADVERT" } }
            )
        );
        Assert.Equal(1, filtered.GetProperty("receptionCount").GetInt64());
        Assert.Equal(from, filtered.GetProperty("lastArrivedAt").GetDateTimeOffset());
        AssertType(
            Assert.Single(filtered.GetProperty("packetTypeCounts").EnumerateArray()),
            "ADVERT"
        );

        var empty = ObserverActivityResult(
            await ReadAsync(observerId.ToString(), new { regionCodes = new[] { " TST " } })
        );
        Assert.Equal("UNKNOWN", empty.GetProperty("historyCoverage").GetString());
        Assert.Equal(0, empty.GetProperty("receptionCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("firstArrivedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastArrivedAt").ValueKind);
        Assert.Empty(empty.GetProperty("packetTypeCounts").EnumerateArray());
        AssertSignal(empty, "snr", 0, null);
        AssertSignal(empty, "rssi", 0, null);
        Assert.All(empty.GetProperty("buckets").EnumerateArray(), bucket =>
            Assert.Equal(0, bucket.GetProperty("receptionCount").GetInt64())
        );
        Assert.Equal(
            "ObserverNotFoundError",
            ObserverActivityResult(await ReadAsync(Guid.NewGuid().ToString(), null))
                .GetProperty("__typename")
                .GetString()
        );
        var beforeRejections = reads.Count;
        AssertTrendError(await ReadAsync("not-a-uuid", null), "INVALID_INPUT");

        const string expensive = """
            query ($observer: ID!, $from: Instant!, $to: Instant!, $filter: ObserverActivityFilterInput) {
              observerActivitySummary(observerId: $observer, from: $from, to: $to, filter: $filter) {
                ... on ObserverNotFoundError { message }
              }
              meshTrafficTrend(from: $from, to: $to) { from }
              ...More
            }
            fragment More on Query { extra: meshTrafficSummary(from: $from, to: $to) { receptionCount } }
            """;
        AssertTrendError(await ReadAsync(observerId.ToString(), null, expensive), "HC0047");
        Assert.Equal(beforeRejections, reads.Count);

        // A reception changes after the totals query, before the mix and buckets. All
        // fields in this response must still use the snapshot that produced the totals.
        reads.BeforeRead = async (number, cancellationToken) =>
        {
            if (number == beforeRejections + 2)
            {
                await db
                    .MeshPacketReceptions.Where(row => row.Id == second)
                    .ExecuteUpdateAsync(
                        set =>
                            set.SetProperty(row => row.ArrivedAt, Instant.FromDateTimeOffset(to)),
                        cancellationToken
                    );
            }
        };
        var consistent = ObserverActivityResult(await ReadAsync(observerId.ToString(), null));
        Assert.Equal(activity, consistent, JsonElement.DeepEquals);
        var refreshed = ObserverActivityResult(await ReadAsync(observerId.ToString(), null));
        Assert.Equal(2, refreshed.GetProperty("receptionCount").GetInt64());

        Task<JsonElement> ReadAsync(
            string id,
            object? filter,
            string query = ObserverActivityQuery
        ) => PostObserverActivityQueryAsync(client, query, id, from, to, filter, token);
    }

    private static Task<JsonElement> PostObserverActivityQueryAsync(
        HttpClient client,
        string query,
        string observerId,
        DateTimeOffset from,
        DateTimeOffset to,
        object? filter,
        CancellationToken cancellationToken
    ) =>
        PostGraphqlQueryAsync(
            client,
            query,
            new
            {
                observer = observerId,
                from = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(from)),
                to = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(to)),
                filter,
            },
            cancellationToken
        );

    private static JsonElement ObserverActivityResult(JsonElement response)
    {
        Assert.False(response.TryGetProperty("errors", out _), response.ToString());
        return response.GetProperty("data").GetProperty("observerActivitySummary");
    }

    private static void AssertType(JsonElement row, string? type)
    {
        Assert.Equal(type, row.GetProperty("packetType").GetString());
        Assert.Equal(1, row.GetProperty("count").GetInt64());
    }

    private static void AssertActivityBucket(
        JsonElement row,
        DateTimeOffset from,
        DateTimeOffset to,
        long count
    )
    {
        Assert.Equal(from, row.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(to, row.GetProperty("to").GetDateTimeOffset());
        Assert.Equal(count, row.GetProperty("receptionCount").GetInt64());
    }
}
