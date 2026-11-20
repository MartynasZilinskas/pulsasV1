using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    private const int OverlapPacketsPerDay = 1_000;

    [PgmqFact]
    public async Task Flood_overlap_stays_within_gateway_limits_with_distinct_wire_packets()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        var start = DateTimeOffset.UnixEpoch.AddYears(55);
        var (a, b) = await SeedFloodOverlapQualificationAsync(database, start, token);

        var sqlTimes = new List<TimeSpan>();
        await using var gateway = BuildTrafficGateway(
            database,
            new TrafficReadInterceptor(measure: sqlTimes.Add)
        );
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };
        var from = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(start));

        AssertFloodOverlap(await ReadAsync(1), 250, 250, 500); // Warm the executor and query shape.
        for (var run = 1; run <= 3; run++)
        {
            sqlTimes.Clear();
            var stopwatch = Stopwatch.StartNew();
            var overlap = await ReadAsync(QualificationDays);
            var elapsed = stopwatch.Elapsed;
            var wirePackets = QualificationDays * OverlapPacketsPerDay;
            AssertFloodOverlap(overlap, wirePackets / 4, wirePackets / 4, wirePackets / 2);
            Assert.Equal(2, sqlTimes.Count);
            output.WriteLine(
                $"observerFloodOverlap 30d run {run}: {wirePackets} wire packets, {wirePackets * 2} receptions, {sqlTimes.Count} SQL reads, max SQL {sqlTimes.Max().TotalMilliseconds:F1} ms, HTTP {elapsed.TotalMilliseconds:F1} ms"
            );
            Assert.All(sqlTimes, duration =>
                Assert.True(duration < TimeSpan.FromSeconds(2), $"SQL read took {duration}.")
            );
            Assert.True(elapsed < TimeSpan.FromSeconds(3), $"Gateway request took {elapsed}.");
        }

        async Task<JsonElement> ReadAsync(int days) =>
            FloodOverlapResult(
                await PostGraphqlQueryAsync(
                    client,
                    FloodOverlapQuery,
                    new
                    {
                        a,
                        b,
                        from,
                        to = InstantPattern.ExtendedIso.Format(
                            Instant.FromDateTimeOffset(start.AddDays(days))
                        ),
                    },
                    token
                )
            );
    }

    // Seed the observers/broker through ingestion. Persist synthetic packet and reception volume
    // directly via EF for this read qualification; the separate overlap smoke test covers ingestion.
    private static async Task<(Guid A, Guid B)> SeedFloodOverlapQualificationAsync(
        TestDatabase database,
        DateTimeOffset start,
        CancellationToken cancellationToken
    )
    {
        using var observerA = AdvertSigningKey.Generate();
        using var observerB = AdvertSigningKey.Generate();
        var seed = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.RawCustom, [0xff])
        );
        var first = await ReceiveAndProcessAsync(
            database,
            observerA,
            seed,
            start.AddSeconds(-1),
            cancellationToken
        );
        var second = await ReceiveAndProcessAsync(
            database,
            observerB,
            seed,
            start.AddSeconds(-1),
            cancellationToken
        );

        await using var db = database.CreateDbContext();
        var seeds = await db
            .MeshPacketReceptions.Where(row => row.Id == first || row.Id == second)
            .ToDictionaryAsync(
                row => row.Id,
                row => new { row.ObserverId, row.BrokerId },
                cancellationToken
            );
        var a = seeds[first].ObserverId;
        var b = seeds[second].ObserverId;
        var broker = seeds[first].BrokerId;
        for (var day = 0; day < QualificationDays; day++)
        {
            var dayStart = Instant.FromDateTimeOffset(start.AddDays(day));
            byte[] payload = [(byte)day];
            var content = new StoredPacketContent
            {
                Id = Guid.CreateVersion7(),
                Type = PacketType.RawCustom,
                Payload = payload,
                PayloadHash = SHA256.HashData(payload),
            };
            db.PacketContents.Add(content);
            for (var index = 0; index < OverlapPacketsPerDay; index++)
            {
                // Distinct encoded paths share a payload, including when packet cardinality is high.
                var frame = PacketCodec.Create(
                    RouteType.Flood,
                    content.Type,
                    payload,
                    [(byte)index, (byte)(index >> 8)],
                    pathWidth: 2
                );
                var raw = PacketCodec.Encode(frame);
                var hash = SHA256.HashData(raw);
                var packetId = Guid.CreateVersion7();
                db.MeshPackets.Add(
                    new MeshPacket
                    {
                        Id = packetId,
                        ContentId = content.Id,
                        Type = frame.PacketType,
                        RouteType = frame.RouteType,
                        PacketHash = hash,
                        ShortHash = Base64Url.EncodeToString(hash),
                        PayloadHash = content.PayloadHash,
                        PathPrefixWidth = 2,
                        RawPacket = raw,
                    }
                );
                // Most packets arrive in a burst; the rest are spread through the day.
                var offset = index < 750 ? index * 2 : 1800 + (index - 750) * 330;
                var arrival = dayStart + Duration.FromSeconds(offset);
                AddReception(packetId, index % 4 == 1 ? b : a, arrival);
                AddReception(packetId, index % 4 == 0 ? a : b, arrival + Duration.FromSeconds(1));
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        return (a, b);

        void AddReception(Guid packetId, Guid observerId, Instant arrivedAt) =>
            db.MeshPacketReceptions.Add(
                new MeshPacketReception
                {
                    Id = Guid.CreateVersion7(),
                    BrokerId = broker,
                    ObserverId = observerId,
                    RegionCode = "TST",
                    PacketId = packetId,
                    ArrivedAt = arrivedAt,
                }
            );
    }
}
