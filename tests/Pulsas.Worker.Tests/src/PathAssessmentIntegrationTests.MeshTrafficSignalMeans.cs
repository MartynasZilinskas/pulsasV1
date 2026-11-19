using Microsoft.EntityFrameworkCore;
using Pulsas.MeshCore.Cryptography;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    [PgmqFact]
    public async Task Mesh_traffic_signal_means_stay_finite_for_extreme_stored_snr()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var from = DateTimeOffset
            .FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 * 60)
            .AddMinutes(-2);
        var raw = SignedAdvertWithFirstHop(advertiser, from, [0x11]);
        var first = await ReceiveAndProcessAsync(database, observer, raw, from, token);
        var second = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            from.AddSeconds(1),
            token
        );
        var third = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            from.AddSeconds(2),
            token
        );
        await using var db = database.CreateDbContext();

        var reads = new TrafficReadInterceptor();
        await using var gateway = BuildTrafficGateway(database, reads);
        gateway.Urls.Add("http://127.0.0.1:0");
        await gateway.StartAsync(token);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
        };

        await SetSnrAsync(first, double.MaxValue);
        await SetSnrAsync(second, double.MaxValue);
        await SetSnrAsync(third, null);
        await AssertMeansAsync(2, double.MaxValue);

        await SetSnrAsync(first, double.MinValue);
        await SetSnrAsync(second, 12d);
        await SetSnrAsync(third, double.MaxValue);
        await AssertMeansAsync(3, 4d);

        await SetSnrAsync(first, double.Epsilon);
        await SetSnrAsync(second, double.Epsilon);
        await SetSnrAsync(third, double.NaN);
        await AssertMeansAsync(2, double.Epsilon);

        await SetSnrAsync(second, 0);
        await SetSnrAsync(third, 0);
        await AssertMeansAsync(3, 0);

        await SetSnrAsync(first, double.MinValue);
        await SetSnrAsync(second, double.PositiveInfinity);
        await SetSnrAsync(third, double.NegativeInfinity);
        await AssertMeansAsync(1, double.MinValue);

        await SetSnrAsync(first, null);
        await AssertMeansAsync(0, null);

        Task SetSnrAsync(Guid id, double? value) =>
            db
                .MeshPacketReceptions.Where(row => row.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Signal.Snr, value), token);

        async Task AssertMeansAsync(long samples, double? expected)
        {
            const string query = """
                query ($from: Instant!, $to: Instant!) {
                  meshTrafficSummary(from: $from, to: $to) { snr { sampleCount average } }
                  meshTrafficTrend(from: $from, to: $to) { buckets { snr { sampleCount average } } }
                }
                """;
            var result = await PostTrafficQueryAsync(
                client,
                query,
                from,
                from.AddSeconds(3),
                null,
                token
            );
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            var data = result.GetProperty("data");
            AssertSignal(data.GetProperty("meshTrafficSummary"), "snr", samples, expected);
            var bucket = Assert.Single(
                data.GetProperty("meshTrafficTrend").GetProperty("buckets").EnumerateArray()
            );
            AssertSignal(bucket, "snr", samples, expected);
        }
    }
}
