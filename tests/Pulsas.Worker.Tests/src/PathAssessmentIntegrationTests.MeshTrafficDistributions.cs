using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    private const string DistributionFields = """
        from to historyCoverage
        frameSizeBytes { sampleCount buckets { lowerInclusive upperExclusive count } }
        snr { sampleCount buckets { lowerInclusive upperExclusive count } }
        rssi { sampleCount buckets { lowerInclusive upperExclusive count } }
        """;

    private const string DistributionQuery = $$"""
        query ($from: Instant!, $to: Instant!, $filter: MeshTrafficFilterInput) {
          meshTrafficDistributions(from: $from, to: $to, filter: $filter) { {{DistributionFields}} }
        }
        """;

    [PgmqFact]
    public async Task Mesh_traffic_distributions_keep_wire_and_reception_samples_separate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var from = DateTimeOffset
            .FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .AddMinutes(-5);
        var to = from.AddMinutes(5);
        var payload = new byte[29];
        var raw = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.RawCustom, payload)
        );
        var variant = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.RawCustom, payload, path: [0x11])
        );
        Assert.Equal(31, raw.Length);
        Assert.Equal(32, variant.Length);
        var first = await ReceiveAndProcessAsync(database, observer, raw, from, token);
        var repeat = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            from.AddSeconds(1),
            token,
            observerOverride: true
        );
        var other = await ReceiveAndProcessAsync(
            database,
            observer,
            variant,
            from.AddSeconds(2),
            token
        );
        await ReceiveAndProcessAsync(database, observer, variant, to, token);
        await ReceiveAsync(
            database,
            observer,
            [0xff],
            Guid.CreateVersion7(),
            from.AddSeconds(3),
            token
        );

        await using var db = database.CreateDbContext();
        await SetSignalsAsync(first, Math.BitDecrement(-5d), -130);
        await SetSignalsAsync(repeat, -5, null);
        await SetSignalsAsync(other, double.NaN, int.MaxValue);

        var reads = new TrafficReadInterceptor(command =>
        {
            Assert.Contains("GROUP BY", command.CommandText, StringComparison.OrdinalIgnoreCase);
            if (command.CommandText.Contains("RawPacket", StringComparison.Ordinal))
            {
                Assert.Matches("(?:octet_)?length\\([^)]*\"RawPacket\"\\)", command.CommandText);
            }
        });
        reads.BeforeRead = async (number, cancellationToken) =>
        {
            if (number == 2)
            {
                await using var workerDb = database.CreateDbContext();
                Assert.True(
                    await CreateProcessor(database, workerDb)
                        .ProcessNextAsync(database.Options.IngestionQueue, cancellationToken)
                );
            }
        };
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        var snapshot = await ReadAsync();
        Assert.Equal(2, reads.Count);
        AssertDistribution(snapshot, "frameSizeBytes", 9, [(null, 32, 1), (32, 64, 1)]);
        AssertDistribution(snapshot, "snr", 12, [(-10, -5, 1), (-5, 0, 1)]);
        AssertDistribution(snapshot, "rssi", 12, [(-130, -120, 1), (-30, null, 1)]);

        var refreshed = await ReadAsync();
        AssertDistribution(refreshed, "frameSizeBytes", 9, [(null, 32, 1), (32, 64, 1)]);
        AssertDistribution(refreshed, "snr", 12, [(-10, -5, 1), (-5, 0, 1), (5, 10, 1)]);
        AssertDistribution(refreshed, "rssi", 12, [(-130, -120, 1), (-80, -70, 1), (-30, null, 1)]);

        var classified = await ReadAsync(new { packetTypes = new[] { "RAW_CUSTOM" } });
        AssertDistribution(classified, "snr", 12, [(-10, -5, 1), (-5, 0, 1)]);
        var empty = await ReadAsync(new { regionCodes = new[] { " TST " } });
        AssertDistribution(empty, "frameSizeBytes", 9, []);
        AssertDistribution(empty, "snr", 12, []);
        AssertDistribution(empty, "rssi", 12, []);

        const string combined = $$"""
            query ($from: Instant!, $to: Instant!) {
              meshTrafficDistributions(from: $from, to: $to) { {{DistributionFields}} }
              meshTrafficTrend(from: $from, to: $to) { {{TrendFields}} }
            }
            """;
        var beforeCombined = reads.Count;
        var together = await PostTrafficQueryAsync(client, combined, from, to, null, token);
        Assert.False(together.TryGetProperty("errors", out _), together.ToString());
        Assert.Equal(beforeCombined + 3, reads.Count);
        var expensive = combined.Insert(
            combined.LastIndexOf('}'),
            " extra: meshTrafficSummary(from: $from, to: $to) { receptionCount }\n"
        );
        AssertTrendError(
            await PostTrafficQueryAsync(client, expensive, from, to, null, token),
            "HC0047"
        );
        Assert.Equal(beforeCombined + 3, reads.Count);

        // Check the full numeric range on the distribution read, which does not average values.
        await SetSignalsAsync(first, double.MinValue, int.MinValue);
        await SetSignalsAsync(repeat, double.MaxValue, null);
        await SetSignalsAsync(other, double.PositiveInfinity, -30);
        var tails = await ReadAsync(new { packetTypes = new[] { "RAW_CUSTOM" } });
        AssertDistribution(tails, "snr", 12, [(null, -30, 1), (20, null, 1)]);
        AssertDistribution(tails, "rssi", 12, [(null, -130, 1), (-30, null, 1)]);

        Task SetSignalsAsync(Guid id, double snr, int? rssi) =>
            db
                .MeshPacketReceptions.Where(row => row.Id == id)
                .ExecuteUpdateAsync(
                    set =>
                        set.SetProperty(row => row.Signal.Snr, (double?)snr)
                            .SetProperty(row => row.Signal.Rssi, rssi),
                    token
                );

        async Task<JsonElement> ReadAsync(object? filter = null)
        {
            var result = await PostTrafficQueryAsync(
                client,
                DistributionQuery,
                from,
                to,
                filter,
                token
            );
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            var data = result.GetProperty("data").GetProperty("meshTrafficDistributions");
            Assert.Equal(from, data.GetProperty("from").GetDateTimeOffset());
            Assert.Equal(to, data.GetProperty("to").GetDateTimeOffset());
            Assert.Equal("UNKNOWN", data.GetProperty("historyCoverage").GetString());
            return data;
        }
    }

    private static void AssertDistribution(
        JsonElement data,
        string name,
        int bucketCount,
        (int? Lower, int? Upper, long Count)[] expected
    )
    {
        var distribution = data.GetProperty(name);
        var buckets = distribution
            .GetProperty("buckets")
            .EnumerateArray()
            .Select(bucket =>
                (
                    Lower: GetEdge(bucket, "lowerInclusive"),
                    Upper: GetEdge(bucket, "upperExclusive"),
                    Count: bucket.GetProperty("count").GetInt64()
                )
            )
            .ToArray();
        Assert.Equal(bucketCount, buckets.Length);
        Assert.Null(buckets[0].Lower);
        Assert.Null(buckets[^1].Upper);
        for (var index = 1; index < buckets.Length; index++)
        {
            Assert.Equal(buckets[index - 1].Upper, buckets[index].Lower);
        }
        Assert.Equal(expected, buckets.Where(bucket => bucket.Count != 0));
        Assert.Equal(
            expected.Sum(bucket => bucket.Count),
            distribution.GetProperty("sampleCount").GetInt64()
        );

        static int? GetEdge(JsonElement bucket, string name) =>
            bucket.GetProperty(name) is { ValueKind: JsonValueKind.Number } value
                ? value.GetInt32()
                : null;
    }
}
