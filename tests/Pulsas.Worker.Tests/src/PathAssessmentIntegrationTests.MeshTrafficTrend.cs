using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.MeshCore.Cryptography;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    private const string TrendFields = """
        from to intervalSeconds historyCoverage
        buckets {
          from to receptionCount wirePacketCount sharedPayloadCount
          snr { sampleCount average } rssi { sampleCount average }
        }
        """;
    private const string TrendQuery = $$"""
        query ($from: Instant!, $to: Instant!, $filter: MeshTrafficFilterInput) {
          meshTrafficTrend(from: $from, to: $to, filter: $filter) { {{TrendFields}} }
        }
        """;

    [PgmqFact]
    public async Task Mesh_traffic_trend_groups_exact_receptions_and_signal_samples_by_utc_arrival()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var now = DateTimeOffset.UtcNow;
        var edge = DateTimeOffset
            .FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 60 * 60)
            .AddMinutes(-5);
        var from = edge.AddTicks(-1);
        var to = edge.AddMinutes(2).AddTicks(1);
        var raw = SignedAdvertWithFirstHop(advertiser, edge, [0x11]);
        var variant = SignedAdvertWithFirstHop(advertiser, edge, [0x22]);
        await ReceiveAndProcessAsync(database, observer, raw, edge.AddTicks(-10), token);
        await ReceiveAndProcessAsync(database, observer, raw, edge, token);
        var repeat = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            edge.AddSeconds(1),
            token
        );
        var otherWire = await ReceiveAndProcessAsync(
            database,
            observer,
            variant,
            edge.AddSeconds(2),
            token
        );
        await ReceiveAndProcessAsync(database, observer, raw, edge.AddMinutes(2), token);
        await ReceiveAndProcessAsync(
            database,
            observer,
            variant,
            edge.AddMinutes(2).AddTicks(10),
            token
        );
        var unlinked = Guid.CreateVersion7();
        await ReceiveAsync(database, observer, [0xff], unlinked, edge.AddSeconds(3), token);

        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );
        await db
            .MeshPacketReceptions.Where(row => row.Id == repeat)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(row => row.Signal.Rssi, (int?)null)
                        .SetProperty(row => row.Signal.Snr, (double?)2.5),
                token
            );
        await db
            .MeshPacketReceptions.Where(row => row.Id == otherWire)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.Signal.Snr, (double?)double.NaN),
                token
            );

        var reads = new TrafficReadInterceptor(command =>
        {
            Assert.Contains("date_bin(", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GROUP BY", command.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "count(DISTINCT",
                command.CommandText,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Contains("avg(", command.CommandText, StringComparison.OrdinalIgnoreCase);
        });
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        var trend = Trend(await PostTrafficQueryAsync(client, TrendQuery, from, to, null, token));
        Assert.Equal(1, reads.Count);
        Assert.Equal(60, trend.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(from, trend.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(to, trend.GetProperty("to").GetDateTimeOffset());

        var buckets = trend.GetProperty("buckets");
        Assert.Equal(4, buckets.GetArrayLength());
        AssertBucket(buckets[0], from, edge, 0, 0, 0);
        AssertBucket(buckets[1], edge, edge.AddMinutes(1), 4, 2, 1);
        AssertSignal(buckets[1], "snr", 3, 6.5);
        AssertSignal(buckets[1], "rssi", 3, -75);
        AssertBucket(buckets[2], edge.AddMinutes(1), edge.AddMinutes(2), 0, 0, 0);
        AssertSignal(buckets[2], "snr", 0, null);
        AssertSignal(buckets[2], "rssi", 0, null);
        AssertBucket(buckets[3], edge.AddMinutes(2), to, 1, 1, 1);

        var filtered = Trend(
            await PostTrafficQueryAsync(
                client,
                TrendQuery,
                from,
                to,
                new { packetTypes = new[] { "ADVERT" } },
                token
            )
        );
        AssertBucket(filtered.GetProperty("buckets")[1], edge, edge.AddMinutes(1), 3, 2, 1);
        var observerId = await db
            .MeshPacketReceptions.Where(row => row.Id == repeat)
            .Select(row => row.ObserverId)
            .SingleAsync(token);
        var empty = Trend(
            await PostTrafficQueryAsync(
                client,
                TrendQuery,
                from,
                to,
                new { observerIds = new[] { observerId }, regionCodes = new[] { " TST " } },
                token
            )
        );
        Assert.Equal(4, empty.GetProperty("buckets").GetArrayLength());
        Assert.All(empty.GetProperty("buckets").EnumerateArray(), bucket =>
            Assert.Equal(0, bucket.GetProperty("receptionCount").GetInt64())
        );
        Assert.Equal(3, reads.Count);
    }

    [PgmqFact]
    public async Task Mesh_traffic_trend_keeps_requested_bounds_when_widening_and_shares_the_read_budget()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var epoch = DateTimeOffset.UnixEpoch;
        var observed = epoch.AddTicks(-10);
        var raw = SignedAdvertWithFirstHop(advertiser, epoch, [0x11]);
        var id = await ReceiveAndProcessAsync(database, observer, raw, epoch, token);
        await using var db = database.CreateDbContext();
        await db
            .MeshPacketReceptions.Where(row => row.Id == id)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.ArrivedAt, Instant.FromDateTimeOffset(observed)),
                token
            );

        var reads = new TrafficReadInterceptor();
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        var alignedFrom = epoch.AddHours(-1);
        var alignedTo = epoch.AddHours(1);
        var aligned = await ReadAsync(alignedFrom, alignedTo);
        Assert.Equal(60, aligned.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(120, aligned.GetProperty("buckets").GetArrayLength());
        AssertBucket(aligned.GetProperty("buckets")[59], epoch.AddMinutes(-1), epoch, 1, 1, 1);
        AssertBucket(aligned.GetProperty("buckets")[60], epoch, epoch.AddMinutes(1), 0, 0, 0);

        var shiftedFrom = alignedFrom.AddTicks(1);
        var shiftedTo = alignedTo.AddTicks(1);
        var shifted = await ReadAsync(shiftedFrom, shiftedTo);
        Assert.Equal(300, shifted.GetProperty("intervalSeconds").GetInt32());
        var shiftedBuckets = shifted.GetProperty("buckets");
        Assert.Equal(25, shiftedBuckets.GetArrayLength());
        AssertBucket(shiftedBuckets[0], shiftedFrom, alignedFrom.AddMinutes(5), 0, 0, 0);
        AssertBucket(shiftedBuckets[11], epoch.AddMinutes(-5), epoch, 1, 1, 1);
        AssertBucket(shiftedBuckets[24], alignedTo, shiftedTo, 0, 0, 0);

        var longest = await ReadAsync(shiftedTo.AddDays(-30), shiftedTo);
        Assert.Equal(28800, longest.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(91, longest.GetProperty("buckets").GetArrayLength());

        const string combined = $$"""
            query ($from: Instant!, $to: Instant!) {
              meshTrafficSummary(from: $from, to: $to) {
                receptionCount wirePacketCount sharedPayloadCount activeObserverCount activeRegionCount
                snr { sampleCount average } rssi { sampleCount average }
                packetTypeCounts { packetType count }
                tags { tag sharedPayloadCount receptionCount }
              }
              meshTrafficTrend(from: $from, to: $to) { {{TrendFields}} }
            }
            """;
        var both = await PostTrafficQueryAsync(
            client,
            combined,
            alignedFrom,
            alignedTo,
            null,
            token
        );
        Assert.Equal(120, Trend(both).GetProperty("buckets").GetArrayLength());
        Assert.Equal(
            1,
            both.GetProperty("data")
                .GetProperty("meshTrafficSummary")
                .GetProperty("receptionCount")
                .GetInt64()
        );

        var beforeRejections = reads.Count;
        var expensive = combined.Insert(
            combined.LastIndexOf('}'),
            " extra: meshTrafficTrend(from: $from, to: $to) { from }\n"
        );
        var rejected = await PostTrafficQueryAsync(
            client,
            expensive,
            alignedFrom,
            alignedTo,
            null,
            token
        );
        AssertTrendError(rejected, "HC0047");
        AssertTrendError(
            await PostTrafficQueryAsync(client, TrendQuery, alignedFrom, alignedFrom, null, token),
            "INVALID_INPUT"
        );
        AssertTrendError(
            await PostTrafficQueryAsync(
                client,
                TrendQuery,
                shiftedTo.AddDays(-30).AddTicks(-1),
                shiftedTo,
                null,
                token
            ),
            "INVALID_INPUT"
        );
        Assert.Equal(beforeRejections, reads.Count);

        async Task<JsonElement> ReadAsync(DateTimeOffset from, DateTimeOffset to) =>
            Trend(await PostTrafficQueryAsync(client, TrendQuery, from, to, null, token));
    }

    private static void AssertTrendError(JsonElement response, string code)
    {
        Assert.True(response.TryGetProperty("errors", out var errors), response.ToString());
        Assert.Equal(
            code,
            Assert
                .Single(errors.EnumerateArray())
                .GetProperty("extensions")
                .GetProperty("code")
                .GetString()
        );
    }

    private static JsonElement Trend(JsonElement result)
    {
        Assert.False(result.TryGetProperty("errors", out _), result.ToString());
        var trend = result.GetProperty("data").GetProperty("meshTrafficTrend");
        Assert.Equal("UNKNOWN", trend.GetProperty("historyCoverage").GetString());
        return trend;
    }

    private static void AssertBucket(
        JsonElement bucket,
        DateTimeOffset from,
        DateTimeOffset to,
        long receptions,
        long wire,
        long payloads
    )
    {
        Assert.Equal(from, bucket.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(to, bucket.GetProperty("to").GetDateTimeOffset());
        Assert.Equal(receptions, bucket.GetProperty("receptionCount").GetInt64());
        Assert.Equal(wire, bucket.GetProperty("wirePacketCount").GetInt64());
        Assert.Equal(payloads, bucket.GetProperty("sharedPayloadCount").GetInt64());
    }

    private static void AssertSignal(JsonElement bucket, string metric, long samples, double? mean)
    {
        var reading = bucket.GetProperty(metric);
        Assert.Equal(samples, reading.GetProperty("sampleCount").GetInt64());
        var average = reading.GetProperty("average");
        Assert.Equal(
            mean,
            average.ValueKind == JsonValueKind.Null ? (double?)null : average.GetDouble()
        );
    }
}
