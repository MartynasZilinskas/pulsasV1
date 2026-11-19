using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Text;
using Pulsas.Gateway.ApiService;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.Persistence;
using Pulsas.Persistence.Packets;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;

namespace Pulsas.Worker.Tests;

public partial class PathAssessmentIntegrationTests
{
    [PgmqFact]
    public async Task Mesh_traffic_summary_counts_filtered_stored_records_and_current_tags()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var from = DateTimeOffset
            .FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600)
            .AddHours(-3);
        var to = from.AddHours(3);
        var raw = SignedAdvertWithFirstHop(advertiser, from, [0x11]);
        var variant = SignedAdvertWithFirstHop(advertiser, from, [0x22]);
        var id = await ReceiveAndProcessAsync(database, observer, raw, from, token);
        var other = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            from.AddHours(1),
            token,
            observerOverride: true
        );
        await ReceiveAndProcessAsync(database, observer, variant, from.AddHours(2), token);
        await ReceiveAndProcessAsync(database, observer, raw, to, token);
        await ReceiveAndProcessAsync(database, observer, raw, from.AddTicks(-10), token);
        await using var db = database.CreateDbContext();
        await db
            .MeshPacketReceptions.Where(row => row.Id == other)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(row => row.Signal.Rssi, (int?)null)
                        .SetProperty(row => row.Signal.Snr, (double?)2.5),
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

        var untagged = await ReadAsync(from, to);
        AssertTotals(untagged, 3, 2, 1);
        Assert.Equal(2, untagged.GetProperty("activeObserverCount").GetInt64());
        Assert.Empty(untagged.GetProperty("tags").EnumerateArray());
        Assert.Equal(3, untagged.GetProperty("snr").GetProperty("sampleCount").GetInt64());
        Assert.Equal(6.5, untagged.GetProperty("snr").GetProperty("average").GetDouble());
        Assert.Equal(2, untagged.GetProperty("rssi").GetProperty("sampleCount").GetInt64());
        Assert.Equal(-75, untagged.GetProperty("rssi").GetProperty("average").GetDouble());

        // The same frame contributes to both arrival buckets, but only once to the summary.
        var trend = Trend(await PostTrafficQueryAsync(client, TrendQuery, from, to, null, token));
        var width = TimeSpan.FromSeconds(trend.GetProperty("intervalSeconds").GetInt32());
        var active = trend
            .GetProperty("buckets")
            .EnumerateArray()
            .Where(bucket => bucket.GetProperty("receptionCount").GetInt64() > 0)
            .ToArray();
        Assert.Collection(
            active,
            bucket => AssertBucket(bucket, from, from + width, 1, 1, 1),
            bucket => AssertBucket(bucket, from.AddHours(1), from.AddHours(1) + width, 1, 1, 1),
            bucket => AssertBucket(bucket, from.AddHours(2), from.AddHours(2) + width, 1, 1, 1)
        );

        var fractional = await ReadAsync(from.AddTicks(1), to.AddTicks(1));
        AssertTotals(fractional, 3, 2, 1);
        Assert.Equal(2, fractional.GetProperty("rssi").GetProperty("sampleCount").GetInt64());
        AssertTotals(await ReadAsync(from.AddTicks(1), to), 2, 2, 1);
        AssertTotals(await ReadAsync(to, to.AddTicks(1)), 1, 1, 1);

        var empty = await ReadAsync(from, to, new { regionCodes = new[] { " TST " } });
        AssertTotals(empty, 0, 0, 0);
        Assert.Empty(empty.GetProperty("tags").EnumerateArray());
        Assert.Equal(0, empty.GetProperty("rssi").GetProperty("sampleCount").GetInt64());
        Assert.Equal(
            JsonValueKind.Null,
            empty.GetProperty("rssi").GetProperty("average").ValueKind
        );

        var fixture = await db
            .MeshPacketReceptions.Where(row => row.Id == id)
            .Select(row => new { row.ObserverId, row.Packet!.ContentId })
            .SingleAsync(token);
        db.PacketContentTags.AddRange(
            new() { ContentId = fixture.ContentId, Tag = "meshmapper" },
            new() { ContentId = fixture.ContentId, Tag = "bot" }
        );
        await db.SaveChangesAsync(token);

        var late = Guid.CreateVersion7();
        await ReceiveAsync(database, observer, [0xff], late, from.AddMinutes(30), token);
        AssertTotals(await ReadAsync(from, to), 3, 2, 1);
        await using var workerDb = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, workerDb)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );
        var withUnlinked = await ReadAsync(from, to);
        AssertTotals(withUnlinked, 4, 2, 1);
        Assert.Equal(4, withUnlinked.GetProperty("snr").GetProperty("sampleCount").GetInt64());
        Assert.Equal(3, withUnlinked.GetProperty("rssi").GetProperty("sampleCount").GetInt64());
        await db
            .MeshPacketReceptions.Where(row => row.Id == late)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.Signal.Snr, (double?)double.NaN),
                token
            );
        var invalidSnr = await ReadAsync(from, to);
        Assert.Equal(3, invalidSnr.GetProperty("snr").GetProperty("sampleCount").GetInt64());
        Assert.Equal(6.5, invalidSnr.GetProperty("snr").GetProperty("average").GetDouble());

        var tagged = await ReadAsync(
            from,
            to,
            new
            {
                observerIds = new[] { fixture.ObserverId },
                regionCodes = new[] { "TST" },
                packetTypes = new[] { "ADVERT" },
            }
        );
        AssertTotals(tagged, 2, 2, 1);
        var tags = tagged.GetProperty("tags").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "bot", "meshmapper" },
            tags.Select(t => t.GetProperty("tag").GetString())
        );
        Assert.All(tags, tag =>
        {
            Assert.Equal(1, tag.GetProperty("sharedPayloadCount").GetInt64());
            Assert.Equal(2, tag.GetProperty("receptionCount").GetInt64());
        });

        // Commit new tags after the totals read has taken its snapshot, but before the tag query.
        var tagRead = reads.Count + 2;
        reads.BeforeRead = async (number, cancellationToken) =>
        {
            if (number == tagRead)
            {
                db.PacketContentTags.AddRange(
                    Enumerable
                        .Range(0, 257)
                        .Select(index => new PacketContentTag
                        {
                            ContentId = fixture.ContentId,
                            Tag = $"tag-{index:D3}",
                        })
                );
                await db.SaveChangesAsync(cancellationToken);
            }
        };
        var snapshot = await ReadAsync(from, to);
        Assert.Equal(tagRead, reads.Count);
        Assert.Equal(2, snapshot.GetProperty("tags").GetArrayLength());
        var refreshed = await ReadAsync(from, to);
        Assert.Equal(259, refreshed.GetProperty("tags").GetArrayLength());
        AssertTotals(refreshed, 4, 2, 1);

        var secondPayload = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvertWithFirstHop(advertiser, from.AddDays(-1), [0x11]),
            from.AddDays(-1),
            token
        );
        var secondContent = await db
            .MeshPacketReceptions.Where(row => row.Id == secondPayload)
            .Select(row => row.Packet!.ContentId)
            .SingleAsync(token);
        Assert.NotEqual(fixture.ContentId, secondContent);
        db.PacketContentTags.Add(new() { ContentId = secondContent, Tag = "bot" });
        await db.SaveChangesAsync(token);
        var month = await ReadAsync(to.AddDays(-30), to);
        AssertTotals(month, 6, 3, 2);
        var bot = month.GetProperty("tags")[0];
        Assert.Equal("bot", bot.GetProperty("tag").GetString());
        Assert.Equal(2, bot.GetProperty("sharedPayloadCount").GetInt64());
        Assert.Equal(5, bot.GetProperty("receptionCount").GetInt64());

        const string comparison = """
            query ($from: Instant!, $to: Instant!) {
              a: meshTrafficSummary(from: $from, to: $to) { receptionCount }
              b: meshTrafficSummary(from: $from, to: $to) { receptionCount }
            }
            """;
        const string thirdRead = """
            fragment Extra on Query {
              c: meshTrafficSummary(from: $from, to: $to) { receptionCount }
            }
            """;
        var beforeBudgetCheck = reads.Count;
        var rejected = await PostAsync(
            comparison.Insert(comparison.LastIndexOf('}'), "...Extra\n") + "\n" + thirdRead,
            from,
            to
        );
        AssertError(rejected, "HC0047");
        AssertError(await PostAsync(comparison, to, to), "INVALID_INPUT");
        AssertError(await PostAsync(comparison, to.AddDays(-30).AddTicks(-1), to), "INVALID_INPUT");
        Assert.Equal(beforeBudgetCheck, reads.Count);

        var two = await PostAsync(comparison, from, to);
        var both = two.GetProperty("data");
        Assert.Equal(4, both.GetProperty("a").GetProperty("receptionCount").GetInt64());
        Assert.Equal(4, both.GetProperty("b").GetProperty("receptionCount").GetInt64());
        Assert.Equal(beforeBudgetCheck + 4, reads.Count);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reads.BeforeRead = async (_, cancellationToken) =>
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
        };
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(token);
        var request = PostAsync(
            """
            query ($from: Instant!, $to: Instant!) {
              meshTrafficSummary(from: $from, to: $to) { receptionCount }
            }
            """,
            from,
            to,
            requestToken: abort.Token
        );
        await entered.Task.WaitAsync(token);
        await abort.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await canceled.Task.WaitAsync(token);
        reads.BeforeRead = null;
        AssertTotals(await ReadAsync(from, to), 4, 2, 1);

        static void AssertError(JsonElement result, string code)
        {
            Assert.True(result.TryGetProperty("errors", out var errors), result.ToString());
            Assert.NotEmpty(errors.EnumerateArray());
            Assert.All(errors.EnumerateArray(), error =>
                Assert.Equal(code, error.GetProperty("extensions").GetProperty("code").GetString())
            );
        }

        async Task<JsonElement> ReadAsync(
            DateTimeOffset start,
            DateTimeOffset end,
            object? filter = null
        )
        {
            const string query = """
                query ($from: Instant!, $to: Instant!, $filter: MeshTrafficFilterInput) {
                  meshTrafficSummary(from: $from, to: $to, filter: $filter) {
                    from to historyCoverage receptionCount wirePacketCount sharedPayloadCount activeObserverCount
                    rssi { average sampleCount } snr { average sampleCount }
                    tags { tag sharedPayloadCount receptionCount }
                  }
                }
                """;
            var result = await PostAsync(query, start, end, filter);
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            var summary = result.GetProperty("data").GetProperty("meshTrafficSummary");
            Assert.Equal(start, summary.GetProperty("from").GetDateTimeOffset());
            Assert.Equal(end, summary.GetProperty("to").GetDateTimeOffset());
            return summary;
        }

        Task<JsonElement> PostAsync(
            string query,
            DateTimeOffset start,
            DateTimeOffset end,
            object? filter = null,
            CancellationToken? requestToken = null
        ) => PostTrafficQueryAsync(client, query, start, end, filter, requestToken ?? token);
    }

    [PgmqFact]
    public async Task Mesh_traffic_summary_counts_active_sources_and_partitions_receptions_by_packet_type()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        await using var db = database.CreateDbContext();
        const string region = " tst ";
        db.Regions.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                Code = region,
                CountryCode = "LT",
            }
        );
        await db.SaveChangesAsync(token);
        using var observer = AdvertSigningKey.Generate();
        using var other = AdvertSigningKey.Generate();
        using var advertiser = AdvertSigningKey.Generate();
        var from = DateTimeOffset
            .FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .AddMinutes(-10);
        var to = from.AddMinutes(10);
        var advert = SignedAdvertWithFirstHop(advertiser, from, [0x11]);
        await ReceiveAndProcessAsync(database, observer, advert, from, token);
        await ReceiveAndProcessAsync(database, observer, advert, from.AddSeconds(1), token);
        byte[] reserved = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, (PacketType)12, [1, 2, 3])
        );
        await ReceiveAndProcessAsync(database, observer, reserved, from.AddSeconds(2), token);

        var key = Convert.ToHexStringLower(other.PublicKey.AsSpan());
        var late = from.AddSeconds(3);
        await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
            new(
                Guid.CreateVersion7(),
                [.. ObserverReports.Packet(new("ignored", "synthetic"), "ff", late)],
                $"meshcore/{region}/{key}/packets",
                "ws://synthetic.invalid/mqtt",
                late
            ),
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

        const string query = """
            query ($from: Instant!, $to: Instant!, $filter: MeshTrafficFilterInput) {
              meshTrafficSummary(from: $from, to: $to, filter: $filter) {
                historyCoverage receptionCount activeObserverCount activeRegionCount
                ...Mix
                mixAgain: packetTypeCounts { count }
              }
            }
            fragment Mix on MeshTrafficSummary { mix: packetTypeCounts { packetType count } }
            """;
        // Persist a new reception after totals, but before the type aggregation takes place.
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
        AssertActivity(await ReadAsync(), 1, 1, [("ADVERT", 2), ("RESERVED12", 1)]);
        Assert.Equal(3, reads.Count); // totals, mix (once despite aliases), tags
        AssertActivity(await ReadAsync(), 2, 2, [("ADVERT", 2), ("RESERVED12", 1), (null, 1)]);
        AssertActivity(await ReadAsync(new { regionCodes = new[] { region } }), 1, 1, [(null, 1)]);
        AssertActivity(await ReadAsync(new { regionCodes = new[] { " TST " } }), 0, 0, []);
        AssertActivity(await ReadAsync(new { regionCodes = new[] { "tst" } }), 0, 0, []);
        AssertActivity(
            await ReadAsync(new { packetTypes = new[] { "ADVERT" } }),
            1,
            1,
            [("ADVERT", 2)]
        );

        async Task<JsonElement> ReadAsync(object? filter = null)
        {
            var result = await PostTrafficQueryAsync(client, query, from, to, filter, token);
            Assert.False(result.TryGetProperty("errors", out _), result.ToString());
            return result.GetProperty("data").GetProperty("meshTrafficSummary");
        }

        static void AssertActivity(
            JsonElement summary,
            long observers,
            long regions,
            (string? Type, long Count)[] expected
        )
        {
            Assert.Equal("UNKNOWN", summary.GetProperty("historyCoverage").GetString());
            Assert.Equal(observers, summary.GetProperty("activeObserverCount").GetInt64());
            Assert.Equal(regions, summary.GetProperty("activeRegionCount").GetInt64());
            Assert.Equal(
                expected.Sum(row => row.Count),
                summary.GetProperty("receptionCount").GetInt64()
            );
            Assert.Equal(
                expected,
                summary
                    .GetProperty("mix")
                    .EnumerateArray()
                    .Select(row =>
                        (
                            row.GetProperty("packetType").GetString(),
                            row.GetProperty("count").GetInt64()
                        )
                    )
            );
            Assert.Equal(
                expected.Select(row => row.Count),
                summary
                    .GetProperty("mixAgain")
                    .EnumerateArray()
                    .Select(row => row.GetProperty("count").GetInt64())
            );
        }
    }

    private static WebApplication BuildTrafficGateway(
        TestDatabase database,
        DbCommandInterceptor reads
    ) =>
        GatewayApplication.Build(
            new WebApplicationOptions { EnvironmentName = "Development" },
            builder =>
            {
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Gateway:EnableReceptionReads"] = "true",
                        ["ConnectionStrings:pulsas"] = database.ConnectionString,
                    }
                );
                builder.Services.ConfigureDbContext<TrackingDbContext>(options =>
                    options.AddInterceptors(reads)
                );
            }
        );

    private static async Task<JsonElement> PostTrafficQueryAsync(
        HttpClient client,
        string query,
        DateTimeOffset start,
        DateTimeOffset end,
        object? filter,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.PostAsJsonAsync(
            "/graphql",
            new
            {
                query,
                variables = new
                {
                    from = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(start)),
                    to = InstantPattern.ExtendedIso.Format(Instant.FromDateTimeOffset(end)),
                    filter,
                },
            },
            cancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var result = JsonDocument.Parse(body);
        return result.RootElement.Clone();
    }

    private class TrafficReadInterceptor(
        Action<DbCommand>? inspect = null,
        Action<TimeSpan>? measure = null
    ) : DbCommandInterceptor
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        public Func<int, CancellationToken, Task>? BeforeRead { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            var number = Interlocked.Increment(ref count);
            inspect?.Invoke(command);
            if (BeforeRead is { } beforeRead)
            {
                await beforeRead(number, cancellationToken);
            }
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            measure?.Invoke(eventData.Duration);
            return ValueTask.FromResult(result);
        }
    }

    private static void AssertTotals(JsonElement result, long receptions, long wire, long payloads)
    {
        Assert.Equal("UNKNOWN", result.GetProperty("historyCoverage").GetString());
        Assert.Equal(receptions, result.GetProperty("receptionCount").GetInt64());
        Assert.Equal(wire, result.GetProperty("wirePacketCount").GetInt64());
        Assert.Equal(payloads, result.GetProperty("sharedPayloadCount").GetInt64());
    }
}
