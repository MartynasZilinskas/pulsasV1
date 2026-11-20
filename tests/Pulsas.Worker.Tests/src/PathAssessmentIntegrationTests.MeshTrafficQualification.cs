using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.Persistence.Receptions;
using Xunit;
using Xunit.Abstractions;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests(ITestOutputHelper output)
{
    private const int ReceptionsPerDay = 2_000;
    private const int QualificationDays = 30;

    [PgmqFact]
    public async Task Mesh_traffic_reads_stay_within_gateway_limits_on_a_synthetic_month()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        var end = DateTimeOffset.UnixEpoch.AddYears(55).AddDays(30);
        var observerId = await SeedTrafficQualificationAsync(
            database,
            end.AddDays(-QualificationDays),
            token
        );

        var commandTimes = new List<TimeSpan>();
        var reads = new TrafficReadInterceptor(measure: commandTimes.Add);
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        const string summaryQuery = """
            query ($from: Instant!, $to: Instant!, $filter: MeshTrafficFilterInput) {
              meshTrafficSummary(from: $from, to: $to, filter: $filter) {
                receptionCount wirePacketCount sharedPayloadCount activeObserverCount activeRegionCount
                snr { sampleCount average } rssi { sampleCount average }
                packetTypeCounts { packetType count }
                tags { tag sharedPayloadCount receptionCount }
              }
            }
            """;
        (string Root, string Query, int Commands)[] queries =
        [
            ("meshTrafficSummary", summaryQuery, 3),
            ("meshTrafficTrend", TrendQuery, 1),
            ("meshTrafficDistributions", DistributionQuery, 2),
        ];
        foreach (var (root, query, commands) in queries)
        {
            await MeasureAsync(root, query, commands, 1, false, 0); // Warm the executor and query shape.
            foreach (var days in new[] { 1, 7, 30 })
            {
                for (var run = 1; run <= 3; run++)
                {
                    await MeasureAsync(root, query, commands, days, false, run);
                    await MeasureAsync(root, query, commands, days, true, run);
                }
            }
        }

        commandTimes.Clear();
        var observerStopwatch = Stopwatch.StartNew();
        var activity = ObserverActivityResult(
            await PostObserverActivityQueryAsync(
                client,
                ObserverActivityQuery,
                observerId.ToString(),
                end.AddDays(-QualificationDays),
                end,
                null,
                token
            )
        );
        var observerElapsed = observerStopwatch.Elapsed;
        var observerReceptions = QualificationDays * ReceptionsPerDay / 8;
        Assert.Equal(observerReceptions, activity.GetProperty("receptionCount").GetInt64());
        var buckets = activity.GetProperty("buckets");
        Assert.InRange(buckets.GetArrayLength(), 1, 120);
        Assert.Equal(
            observerReceptions,
            buckets.EnumerateArray().Sum(bucket => bucket.GetProperty("receptionCount").GetInt64())
        );
        Assert.Equal(3, commandTimes.Count);
        output.WriteLine(
            $"observerActivitySummary 30d: {observerReceptions} rows, {commandTimes.Count} SQL reads, max SQL {commandTimes.Max().TotalMilliseconds:F1} ms, HTTP {observerElapsed.TotalMilliseconds:F1} ms"
        );

        async Task MeasureAsync(
            string root,
            string query,
            int commands,
            int days,
            bool selective,
            int run
        )
        {
            commandTimes.Clear();
            var stopwatch = Stopwatch.StartNew();
            var result = await PostTrafficQueryAsync(
                client,
                query,
                end.AddDays(-days),
                end,
                selective ? new { observerIds = new[] { observerId } } : null,
                token
            );
            var elapsed = stopwatch.Elapsed;
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            Assert.Equal(commands, commandTimes.Count);
            var data = result.GetProperty("data").GetProperty(root);
            var expectedReceptions = days * ReceptionsPerDay / (selective ? 8 : 1);
            switch (root)
            {
                case "meshTrafficSummary":
                    Assert.Equal(expectedReceptions, data.GetProperty("receptionCount").GetInt64());
                    break;
                case "meshTrafficTrend":
                    Assert.Equal(
                        expectedReceptions,
                        data.GetProperty("buckets")
                            .EnumerateArray()
                            .Sum(bucket => bucket.GetProperty("receptionCount").GetInt64())
                    );
                    break;
                default:
                    Assert.Equal(
                        128,
                        data.GetProperty("frameSizeBytes").GetProperty("sampleCount").GetInt64()
                    );
                    Assert.Equal(
                        expectedReceptions * 9 / 10,
                        data.GetProperty("snr").GetProperty("sampleCount").GetInt64()
                    );
                    Assert.Equal(
                        expectedReceptions * 4 / 5,
                        data.GetProperty("rssi").GetProperty("sampleCount").GetInt64()
                    );
                    break;
            }
            if (run > 0)
            {
                output.WriteLine(
                    $"{root} {days}d {(selective ? "filtered" : "all")} run {run}: {expectedReceptions} rows, {commands} SQL reads, max SQL {commandTimes.Max().TotalMilliseconds:F1} ms, HTTP {elapsed.TotalMilliseconds:F1} ms"
                );
            }
        }
    }

    // Ingest the canonical identities normally; insert only the bulk reception fixture via EF.
    private static async Task<Guid> SeedTrafficQualificationAsync(
        TestDatabase database,
        DateTimeOffset start,
        CancellationToken cancellationToken
    )
    {
        using var observer = AdvertSigningKey.Generate();
        var firstReception = Guid.Empty;
        for (var index = 0; index < 128; index++)
        {
            var raw = PacketCodec.Encode(
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.RawCustom,
                    [(byte)(index / 4), 1, 2, 3],
                    path: [(byte)(index % 4)]
                )
            );
            firstReception = await ReceiveAndProcessAsync(
                database,
                observer,
                raw,
                start.AddSeconds(-index - 1),
                cancellationToken,
                observerOverride: index is > 0 and < 8
            );
        }

        await using var db = database.CreateDbContext();
        var packetIds = await db
            .MeshPackets.OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var observers = await db
            .Observers.OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var broker = await db
            .MeshPacketReceptions.Where(row => row.Id == firstReception)
            .Select(row => row.BrokerId)
            .SingleAsync(cancellationToken);
        Assert.Equal(128, packetIds.Length);
        Assert.Equal(8, observers.Length);
        for (var day = 0; day < QualificationDays; day++)
        {
            var dayStart = Instant.FromDateTimeOffset(start.AddDays(day));
            db.MeshPacketReceptions.AddRange(
                Enumerable
                    .Range(0, ReceptionsPerDay)
                    .Select(index =>
                    {
                        var packet = (index / 8 + index % 8 + day) % packetIds.Length;
                        // 75% of arrivals in a 30-minute burst, the rest spread across the day.
                        var offset = index < 1500 ? index : (index - 1500) * 168 + 1800;
                        return new MeshPacketReception
                        {
                            Id = Guid.CreateVersion7(),
                            BrokerId = broker,
                            ObserverId = observers[index % observers.Length],
                            RegionCode = "TST",
                            PacketId = packetIds[packet],
                            ArrivedAt = dayStart + Duration.FromSeconds(offset),
                            Signal = new()
                            {
                                Snr = index / 8 % 10 == 0 ? null : index % 41 - 20.5,
                                Rssi = index / 8 % 10 is 3 or 4 ? null : -125 + index % 85,
                            },
                        };
                    })
            );
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        return observers[0];
    }
}
