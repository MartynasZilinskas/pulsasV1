using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.Persistence.Regions;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    private const string FloodOverlapQuery = """
        query ($a: ID!, $b: ID!, $from: Instant!, $to: Instant!, $filter: ObserverActivityFilterInput) {
          observerFloodOverlap(observerAId: $a, observerBId: $b, from: $from, to: $to, filter: $filter) {
            __typename
            ... on ObserverFloodOverlap {
              observerAId observerBId from to historyCoverage
              onlyACount onlyBCount bothCount unionCount
            }
            ... on OverlapObserversNotFoundError { message missingObserverIds }
          }
        }
        """;

    [PgmqFact]
    public async Task Flood_overlap_partitions_exact_wire_packets_from_stored_receptions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observerA = AdvertSigningKey.Generate();
        using var observerB = AdvertSigningKey.Generate();
        using var sender = AdvertSigningKey.Generate();
        var from = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var to = from.AddMinutes(2);
        var onlyA = SignedAdvertWithFirstHop(sender, from, [0x11]);
        var onlyB = SignedAdvertWithFirstHop(sender, from, [0x22]);
        var both = SignedAdvertWithFirstHop(sender, from, [0x33]);
        var first = await ReceiveAndProcessAsync(database, observerA, onlyA, from, token);
        var second = await ReceiveAndProcessAsync(database, observerB, onlyB, from, token);
        await ReceiveAndProcessAsync(database, observerA, both, from.AddSeconds(1), token);
        var sharedB = await ReceiveAndProcessAsync(
            database,
            observerB,
            both,
            from.AddSeconds(2),
            token
        );
        await ReceiveAndProcessAsync(database, observerA, onlyA, from.AddSeconds(3), token);
        await ReceiveAndProcessAsync(database, observerB, onlyA, to, token);
        await ReceiveAndProcessAsync(database, observerA, onlyB, from.AddTicks(-10), token);
        var transport = PacketCodec.Encode(
            PacketCodec.Create(
                RouteType.TransportFlood,
                PacketType.RawCustom,
                [1, 2],
                transportCodes: [1, 2, 3, 4]
            )
        );
        await ReceiveAndProcessAsync(database, observerA, transport, from.AddSeconds(4), token);
        var direct = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Direct, PacketType.RawCustom, [3, 4])
        );
        await ReceiveAndProcessAsync(database, observerB, direct, from.AddSeconds(5), token);
        await ReceiveAsync(database, observerB, [0xff], Guid.NewGuid(), from.AddSeconds(6), token);

        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );
        var trace = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.Trace, [1, 2, 3])
        );
        await ReceiveAndProcessAsync(database, observerA, trace, from.AddSeconds(7), token);
        db.Regions.Add(
            new StoredRegion
            {
                Id = Guid.CreateVersion7(),
                Code = "ALT",
                CountryCode = "LT",
            }
        );
        await db.SaveChangesAsync(token);
        Assert.Equal(
            1,
            await db
                .MeshPacketReceptions.Where(row => row.Id == sharedB)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.RegionCode, "ALT"), token)
        );
        var ids = await db
            .MeshPacketReceptions.Where(row => row.Id == first || row.Id == second)
            .ToDictionaryAsync(row => row.Id, row => row.ObserverId, token);
        var a = ids[first];
        var b = ids[second];
        var reads = new TrafficReadInterceptor();
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        var overlap = FloodOverlapResult(await ReadAsync(a, b));
        Assert.Equal(2, reads.Count);
        Assert.Equal(a, overlap.GetProperty("observerAId").GetGuid());
        Assert.Equal(b, overlap.GetProperty("observerBId").GetGuid());
        Assert.Equal(from, overlap.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(to, overlap.GetProperty("to").GetDateTimeOffset());
        AssertFloodOverlap(overlap, 3, 1, 1);
        AssertFloodOverlap(FloodOverlapResult(await ReadAsync(b, a)), 1, 3, 1);
        AssertFloodOverlap(
            FloodOverlapResult(
                await ReadAsync(
                    a,
                    b,
                    new { regionCodes = new[] { "TST" }, packetTypes = new[] { "ADVERT" } }
                )
            ),
            2,
            1,
            0
        );
        AssertFloodOverlap(
            FloodOverlapResult(
                await ReadAsync(
                    a,
                    b,
                    new
                    {
                        regionCodes = new[] { "TST", "ALT" },
                        packetTypes = new[] { "TRACE", "ADVERT" },
                    }
                )
            ),
            2,
            1,
            1
        );
        AssertFloodOverlap(
            FloodOverlapResult(await ReadAsync(a, b, new { packetTypes = new[] { "TRACE" } })),
            1,
            0,
            0
        );
        AssertFloodOverlap(
            FloodOverlapResult(
                await ReadAsync(a, b, new { regionCodes = new[] { "tst", " TST " } })
            ),
            0,
            0,
            0
        );

        var missingA = Guid.NewGuid();
        var missingB = Guid.NewGuid();
        AssertMissingObservers(FloodOverlapResult(await ReadAsync(missingA, b)), [missingA]);
        AssertMissingObservers(FloodOverlapResult(await ReadAsync(a, missingB)), [missingB]);
        AssertMissingObservers(
            FloodOverlapResult(await ReadAsync(missingB, missingA)),
            [missingB, missingA]
        );
        var beforeInvalid = reads.Count;
        AssertTrendError(await ReadAsync(a, a.ToString("B")), "INVALID_INPUT");
        AssertTrendError(await ReadAsync(a, "bad"), "INVALID_INPUT");
        AssertTrendError(await ReadAsync(a, b, until: from), "INVALID_INPUT");
        AssertTrendError(
            await ReadAsync(a, b, until: from.AddDays(30).AddTicks(1)),
            "INVALID_INPUT"
        );
        Assert.Equal(beforeInvalid, reads.Count);

        var beforeConcurrentChange = reads.Count;
        reads.BeforeRead = async (number, cancellationToken) =>
        {
            if (number == beforeConcurrentChange + 2)
            {
                Assert.Equal(
                    1,
                    await db
                        .MeshPacketReceptions.Where(row => row.Id == second)
                        .ExecuteUpdateAsync(
                            set =>
                                set.SetProperty(
                                    row => row.ArrivedAt,
                                    Instant.FromDateTimeOffset(to)
                                ),
                            cancellationToken
                        )
                );
            }
        };
        AssertFloodOverlap(FloodOverlapResult(await ReadAsync(a, b)), 3, 1, 1);
        AssertFloodOverlap(FloodOverlapResult(await ReadAsync(a, b)), 3, 0, 1);

        const string errorOnly = """
            query ($a: ID!, $b: ID!, $from: Instant!, $to: Instant!) {
              a: observerFloodOverlap(observerAId: $a, observerBId: $b, from: $from, to: $to) { ...Missing }
              ...More
            }
            fragment More on Query {
              b: observerFloodOverlap(observerAId: $a, observerBId: $b, from: $from, to: $to) { ...Missing }
              c: observerFloodOverlap(observerAId: $a, observerBId: $b, from: $from, to: $to) { ...Missing }
            }
            fragment Missing on ObserverFloodOverlapResult { ... on OverlapObserversNotFoundError { message } }
            """;
        var two = errorOnly.Replace(
            "c: observerFloodOverlap(observerAId: $a, observerBId: $b, from: $from, to: $to) { ...Missing }",
            "",
            StringComparison.Ordinal
        );
        var beforeTwo = reads.Count;
        var accepted = await ReadAsync(missingA, b, query: two);
        Assert.False(accepted.TryGetProperty("errors", out _), accepted.ToString());
        foreach (var alias in new[] { "a", "b" })
        {
            Assert.Equal(
                "One or both observers were not found.",
                accepted.GetProperty("data").GetProperty(alias).GetProperty("message").GetString()
            );
        }
        Assert.Equal(beforeTwo + 2, reads.Count);
        var beforeCostCheck = reads.Count;
        AssertTrendError(await ReadAsync(a, b, query: errorOnly), "HC0047");
        Assert.Equal(beforeCostCheck, reads.Count);

        var withSibling = FloodOverlapQuery.Insert(
            FloodOverlapQuery.LastIndexOf('}'),
            "  sibling: __typename\n"
        );
        var invalid = await ReadAsync(a, "bad", query: withSibling);
        AssertTrendError(invalid, "INVALID_INPUT");
        AssertFailedOverlapWithSibling(invalid);

        var aggregateRead = reads.Count + 2;
        reads.BeforeRead = (number, _) =>
            number == aggregateRead
                ? throw new InvalidOperationException("Synthetic aggregate read failure")
                : Task.CompletedTask;
        AssertFailedOverlapWithSibling(await ReadAsync(a, b, query: withSibling));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        aggregateRead = reads.Count + 2;
        reads.BeforeRead = async (number, cancellationToken) =>
        {
            if (number == aggregateRead)
            {
                entered.SetResult();
                try
                {
                    await blocked.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled.SetResult();
                    throw;
                }
            }
        };
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(token);
        var request = ReadAsync(a, b, requestToken: abort.Token);
        await entered.Task.WaitAsync(token);
        await abort.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await canceled.Task.WaitAsync(token);
        reads.BeforeRead = null;
        AssertFloodOverlap(FloodOverlapResult(await ReadAsync(a, b)), 3, 0, 1);

        Task<JsonElement> ReadAsync(
            object observerAId,
            object observerBId,
            object? filter = null,
            DateTimeOffset? until = null,
            string query = FloodOverlapQuery,
            CancellationToken? requestToken = null
        ) =>
            PostGraphqlQueryAsync(
                client,
                query,
                new
                {
                    a = observerAId,
                    b = observerBId,
                    from = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(from)),
                    to = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(until ?? to)),
                    filter,
                },
                requestToken ?? token
            );
    }

    private static void AssertFailedOverlapWithSibling(JsonElement response)
    {
        var error = Assert.Single(response.GetProperty("errors").EnumerateArray());
        Assert.Equal(
            "observerFloodOverlap",
            Assert.Single(error.GetProperty("path").EnumerateArray()).GetString()
        );
        var data = response.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("observerFloodOverlap").ValueKind);
        Assert.Equal("Query", data.GetProperty("sibling").GetString());
    }

    private static JsonElement FloodOverlapResult(JsonElement response)
    {
        Assert.False(response.TryGetProperty("errors", out _), response.ToString());
        return response.GetProperty("data").GetProperty("observerFloodOverlap");
    }

    private static void AssertFloodOverlap(JsonElement overlap, long onlyA, long onlyB, long both)
    {
        Assert.Equal("ObserverFloodOverlap", overlap.GetProperty("__typename").GetString());
        Assert.Equal("UNKNOWN", overlap.GetProperty("historyCoverage").GetString());
        Assert.Equal(onlyA, overlap.GetProperty("onlyACount").GetInt64());
        Assert.Equal(onlyB, overlap.GetProperty("onlyBCount").GetInt64());
        Assert.Equal(both, overlap.GetProperty("bothCount").GetInt64());
        Assert.Equal(onlyA + onlyB + both, overlap.GetProperty("unionCount").GetInt64());
    }

    private static void AssertMissingObservers(JsonElement overlap, Guid[] missing)
    {
        Assert.Equal(
            "OverlapObserversNotFoundError",
            overlap.GetProperty("__typename").GetString()
        );
        Assert.Equal(
            missing,
            overlap.GetProperty("missingObserverIds").EnumerateArray().Select(id => id.GetGuid())
        );
    }
}
