using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.Persistence;
using Pulsas.Persistence.Nodes;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService;
using Xunit;
using static Pulsas.Persistence.Nodes.IAdvertNodeManager;

namespace Pulsas.Worker.Tests;

// Uses the retained database and removes only rows owned by each random signing key.
public class AdvertIntegrationTests
{
    private class DatabaseFactory(SaveChangesInterceptor? interceptor = null)
        : IDbContextFactory<TrackingDbContext>
    {
        public TrackingDbContext CreateDbContext()
        {
            var options = TrackingDatabase.Configure(
                new DbContextOptionsBuilder<TrackingDbContext>(),
                Environment.GetEnvironmentVariable("PULSAS_GEOGRAPHY_TEST_CONNECTION")!
            );
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
            return new(options.Options);
        }
    }

    private static readonly DatabaseFactory Databases = new();
    private static readonly DateTimeOffset At = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly uint Timestamp = (uint)At.ToUnixTimeSeconds();

    private static AdvertPayload Signed(
        AdvertSigningKey key,
        uint timestamp,
        AdvertAppData? data
    ) =>
        AdvertCodec.CreateSigned(
            key,
            timestamp,
            AdvertAppDataCodec
                .Encode(data)
                .Match(
                    left: error => throw new InvalidOperationException(error.ToString()),
                    right: bytes => bytes
                )
        );

    private static async Task<MeshNode> Read(byte[] key)
    {
        await using var database = Databases.CreateDbContext();
        return await database.MeshNodes.AsNoTracking().SingleAsync(node => node.PublicKey == key);
    }

    private static async Task Remove(byte[] key)
    {
        await using var database = Databases.CreateDbContext();
        await database.MeshNodes.Where(node => node.PublicKey == key).ExecuteDeleteAsync();
    }

    private class CreationRace : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int writers;
        public int Collisions;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref writers) == 2)
            {
                ready.TrySetResult();
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref Collisions);
            return Task.CompletedTask;
        }
    }

    [GeographyFact]
    public async Task Competing_identity_insert_reports_conflict_for_delivery_owned_retry()
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var race = new CreationRace();
        var manager = new AdvertNodeManager(new DatabaseFactory(race), SystemClock.Instance);
        try
        {
            var outcomes = await Task.WhenAll(
                new[] { 0u, 1u }.Select(offset =>
                    manager.ProcessAdvertAsync(
                        new(
                            Signed(
                                key,
                                Timestamp + offset,
                                new(NodeRole.Repeater, Name: $"winner-{offset}")
                            ),
                            At
                        ),
                        CancellationToken.None
                    )
                )
            );
            Assert.Equal(1, race.Collisions);
            Assert.Single(outcomes, outcome =>
                outcome.Match(
                    left: error => error == ProcessAdvertError.ConcurrentNodeCreation,
                    right: _ => false
                )
            );
            Assert.Single(outcomes, outcome => outcome.Match(left: _ => false, right: _ => true));
            // A new delivery attempt uses the original receipt time, not a manager retry.
            AssertSuccess(
                await manager.ProcessAdvertAsync(
                    new(Signed(key, Timestamp + 1, new(NodeRole.Repeater, Name: "winner-1")), At),
                    CancellationToken.None
                )
            );
            var node = await Read(publicKey);
            Assert.Equal("winner-1", node.Name);
            Assert.Equal((long)Timestamp + 1, node.AdvertMetadataTimestamp);
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyFact]
    public async Task Handler_projects_generator_advert_and_real_regions_without_packet_evidence()
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
        var handler = new MqttPacketReportHandler(
            new PacketProcessingManager(),
            manager,
            NullLogger<MqttPacketReportHandler>.Instance
        );
        var observer = new ObserverIdentity(new string('b', 64), "test-observer");
        try
        {
            var codes = new[] { "KUN", "PLQ", "SQQ", "VNO" };
            for (var round = 1; round <= 4; round++)
            {
                var at = At.AddSeconds(round);
                await handler.HandleAsync(
                    $"meshcore/KUN/{observer.PublicKey}/packets",
                    ObserverReports.Packet(
                        observer,
                        ObserverReports.CreateAdvert(key, round, at),
                        at
                    ),
                    at,
                    CancellationToken.None
                );
                var node = await Read(publicKey);
                await using var database = Databases.CreateDbContext();
                Assert.Equal(
                    codes[round - 1],
                    (
                        await database.Regions.SingleAsync(region =>
                            region.Id == node.LocationRegionId
                        )
                    ).Code
                );
                Assert.Equal($"synthetic-{round}", node.Name);
                Assert.Equal(NodeRole.Repeater, node.Role);
                Assert.Equal(MeshNodeSourceType.ObservedAdvert, node.SourceType);
                Assert.Equal((long)Timestamp + round, node.AdvertMetadataTimestamp);
                Assert.Null(node.LatestAdvertPacketId);
                Assert.Null(node.LatestAdvertHeardAt);
            }
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyFact]
    public async Task Stale_equal_omitted_and_reset_claims_preserve_required_state()
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
        async Task Apply(uint time, AdvertAppData data) =>
            AssertSuccess(
                await manager.ProcessAdvertAsync(
                    new(Signed(key, time, data), At),
                    CancellationToken.None
                )
            );
        try
        {
            await Apply(Timestamp, new(NodeRole.Repeater, 54_736_798, 23_655_660, Name: "first"));
            var first = await Read(publicKey);
            Assert.NotNull(first.LocationRegionId);
            await Apply(Timestamp, new(NodeRole.RoomServer, 0, 0, Name: "equal"));
            await Apply(Timestamp - 1, new(NodeRole.RoomServer, 0, 0, Name: "stale"));
            var unchanged = await Read(publicKey);
            Assert.Equal(first.ModifiedAt, unchanged.ModifiedAt);
            Assert.Equal("first", unchanged.Name);
            Assert.Equal(first.Location, unchanged.Location);
            await Apply(Timestamp + 1, new(NodeRole.RoomServer));
            var omitted = await Read(publicKey);
            Assert.Equal("first", omitted.Name);
            Assert.Equal(first.Location, omitted.Location);
            Assert.Equal(first.LocationRegionId, omitted.LocationRegionId);
            Assert.Equal(NodeRole.RoomServer, omitted.Role);
            await Apply(Timestamp + 2, new(NodeRole.Repeater, 0, 0));
            var reset = await Read(publicKey);
            Assert.Null(reset.Location);
            Assert.Null(reset.LocationRegionId);
            await Apply(Timestamp + 3, new(NodeRole.Repeater, 0, 30_000_000, Name: ""));
            var equator = await Read(publicKey);
            Assert.Equal(0, equator.Location!.Y);
            Assert.Equal(30, equator.Location.X);
            Assert.Equal("", equator.Name);
            await Apply(Timestamp + 4, new(NodeRole.Repeater, 45_000_000, 0));
            var meridian = await Read(publicKey);
            Assert.Equal(45, meridian.Location!.Y);
            Assert.Equal(0, meridian.Location.X);
            Assert.Equal(first.Id, meridian.Id);
            Assert.Equal(first.CreatedAt, meridian.CreatedAt);
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyTheory]
    [InlineData("empty")]
    [InlineData("truncated")]
    [InlineData("bad-utf8")]
    [InlineData("unsupported")]
    [InlineData("latitude")]
    [InlineData("longitude")]
    [InlineData("nul")]
    [InlineData("future")]
    public async Task Unusable_metadata_creates_identity_but_does_not_advance_watermark(string kind)
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
        var advert = kind switch
        {
            "empty" => Signed(key, Timestamp + 1, null),
            "truncated" => AdvertCodec.CreateSigned(key, Timestamp + 1, [0x12, 0]),
            "bad-utf8" => AdvertCodec.CreateSigned(key, Timestamp + 1, [0x82, 0xff]),
            "unsupported" => AdvertCodec.CreateSigned(key, Timestamp + 1, [0x02, 0xff]),
            "latitude" => Signed(key, Timestamp + 1, new(NodeRole.Repeater, 90_000_001, 1)),
            "longitude" => Signed(key, Timestamp + 1, new(NodeRole.Repeater, 1, -180_000_001)),
            "nul" => Signed(key, Timestamp + 1, new(NodeRole.Repeater, Name: "bad\0name")),
            _ => Signed(key, Timestamp + 301, new(NodeRole.Repeater, Name: "future")),
        };
        try
        {
            AssertSuccess(
                await manager.ProcessAdvertAsync(new(advert, At), CancellationToken.None)
            );
            var identity = await Read(publicKey);
            Assert.Null(identity.Name);
            Assert.Null(identity.Role);
            Assert.Null(identity.Location);
            Assert.Null(identity.AdvertMetadataTimestamp);
            var good = Signed(
                key,
                Timestamp,
                new(NodeRole.Repeater, 54_736_798, 23_655_660, Name: "good")
            );
            AssertSuccess(await manager.ProcessAdvertAsync(new(good, At), CancellationToken.None));
            AssertSuccess(
                await manager.ProcessAdvertAsync(new(advert, At), CancellationToken.None)
            );
            var preserved = await Read(publicKey);
            Assert.Equal("good", preserved.Name);
            Assert.Equal((long)Timestamp, preserved.AdvertMetadataTimestamp);
            Assert.NotNull(preserved.LocationRegionId);
            Assert.Equal(identity.Id, preserved.Id);
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyFact]
    public async Task Signature_gate_and_cancellation_are_enforced_at_manager_boundary()
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
        var valid = Signed(key, Timestamp, new(NodeRole.Repeater, Name: "valid"));
        var tampered = valid with { Timestamp = Timestamp + 1 };
        try
        {
            AssertError(
                ProcessAdvertError.InvalidSignature,
                await manager.ProcessAdvertAsync(new(tampered, At), CancellationToken.None)
            );
            await using (var database = Databases.CreateDbContext())
            {
                Assert.False(
                    await database.MeshNodes.AnyAsync(node => node.PublicKey == publicKey)
                );
            }
            AssertSuccess(await manager.ProcessAdvertAsync(new(valid, At), CancellationToken.None));
            AssertError(
                ProcessAdvertError.InvalidSignature,
                await manager.ProcessAdvertAsync(new(tampered, At), CancellationToken.None)
            );
            Assert.Equal("valid", (await Read(publicKey)).Name);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                manager.ProcessAdvertAsync(new(valid, At), cancellation.Token)
            );
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyFact]
    public async Task Concurrent_upserts_keep_newest_metadata_and_existing_identity_source()
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
        try
        {
            // Seed the identity so these calls exercise concurrent updates rather than insertion conflicts.
            AssertSuccess(
                await manager.ProcessAdvertAsync(
                    new(Signed(key, Timestamp - 1, null), At),
                    CancellationToken.None
                )
            );
            var outcomes = await Task.WhenAll(
                Enumerable
                    .Range(0, 16)
                    .Select(index =>
                        manager.ProcessAdvertAsync(
                            new(
                                Signed(
                                    key,
                                    Timestamp + (uint)index,
                                    new(NodeRole.Repeater, Name: $"version-{index}")
                                ),
                                At
                            ),
                            CancellationToken.None
                        )
                    )
            );
            Assert.All(outcomes, AssertSuccess);
            var node = await Read(publicKey);
            Assert.Equal("version-15", node.Name);
            Assert.Equal((long)Timestamp + 15, node.AdvertMetadataTimestamp);
            await using (var database = Databases.CreateDbContext())
            {
                await database
                    .MeshNodes.Where(item => item.Id == node.Id)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(item => item.SourceType, MeshNodeSourceType.MeshCoreMap)
                    );
            }
            AssertSuccess(
                await manager.ProcessAdvertAsync(
                    new(Signed(key, Timestamp + 16, new(NodeRole.Repeater, Name: "updated")), At),
                    CancellationToken.None
                )
            );
            var updated = await Read(publicKey);
            Assert.Equal("updated", updated.Name);
            Assert.Equal((long)Timestamp + 16, updated.AdvertMetadataTimestamp);
            Assert.Equal(node.Id, updated.Id);
            Assert.Equal(node.CreatedAt, updated.CreatedAt);
            Assert.Equal(MeshNodeSourceType.MeshCoreMap, updated.SourceType);
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    [GeographyTheory]
    [InlineData(-90_000_000, -180_000_000)]
    [InlineData(90_000_000, 180_000_000)]
    public async Task Geographic_and_future_tolerance_boundaries_are_inclusive(
        int latitude,
        int longitude
    )
    {
        using var key = AdvertSigningKey.Generate();
        var publicKey = key.PublicKey.ToArray();
        try
        {
            var manager = new AdvertNodeManager(Databases, SystemClock.Instance);
            AssertSuccess(
                await manager.ProcessAdvertAsync(
                    new(
                        Signed(key, Timestamp + 300, new(NodeRole.Repeater, latitude, longitude)),
                        At
                    ),
                    CancellationToken.None
                )
            );
            var node = await Read(publicKey);
            Assert.Equal(latitude / 1e6, node.Location!.Y);
            Assert.Equal(longitude / 1e6, node.Location.X);
        }
        finally
        {
            await Remove(publicKey);
        }
    }

    private static void AssertSuccess(Either<ProcessAdvertError, Unit> outcome) =>
        outcome.Switch(
            left: error => Assert.Fail($"Expected success, got {error}."),
            right: _ => { }
        );

    private static void AssertError(
        ProcessAdvertError expected,
        Either<ProcessAdvertError, Unit> outcome
    ) =>
        outcome.Switch(
            left: error => Assert.Equal(expected, error),
            right: _ => Assert.Fail($"Expected {expected}.")
        );
}
