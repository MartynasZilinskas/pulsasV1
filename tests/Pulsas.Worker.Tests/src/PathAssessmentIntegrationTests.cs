using System.Data.Common;
using System.Text.Json;
using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgmq;
using Npgsql;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.PacketProcessing.Core.Paths;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.Persistence;
using Pulsas.Persistence.Ingestion;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Paths;
using Pulsas.Persistence.Regions;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService.Chat;
using Pulsas.Worker.ApiService.Configuration;
using Pulsas.Worker.ApiService.Delivery;
using Pulsas.Worker.Tests.Simulation;
using Xunit;

namespace Pulsas.Worker.Tests;

public class PathAssessmentIntegrationTests
{
    [PgmqFact]
    public async Task Signed_advert_flows_through_queues_and_archives_reception_snapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        using var sender = AdvertSigningKey.Generate();
        using var observer = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;
        var raw = ObserverReports.CreateAdvert(sender, 1, at);
        var receptionId = Guid.CreateVersion7();

        await using (var db = database.CreateDbContext())
        {
            await db.Regions.AddAsync(
                new StoredRegion
                {
                    Id = Guid.CreateVersion7(),
                    Code = "TST",
                    CountryCode = "LT",
                },
                cancellationToken
            );
            await db.SaveChangesAsync(cancellationToken);
            await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
                new(
                    receptionId,
                    [
                        .. ObserverReports.Packet(
                            new(
                                Convert.ToHexStringLower(observer.PublicKey.AsSpan()),
                                "synthetic-observer"
                            ),
                            raw,
                            at
                        ),
                    ],
                    "meshcore/TST/"
                        + Convert.ToHexStringLower(observer.PublicKey.AsSpan())
                        + "/packets",
                    "ws://synthetic.invalid/mqtt",
                    at
                ),
                cancellationToken
            );

            var settings = database.Options;
            var processor = new ReportQueueProcessor(
                db,
                new PacketProcessingManager(),
                new ReportIngestionManager(db, SystemClock.Instance),
                new AdvertNodeManager(db, SystemClock.Instance),
                new GroupTextManager(
                    db,
                    PacketConfiguration.Read(new ConfigurationBuilder().Build()),
                    SystemClock.Instance
                ),
                Options.Create(settings),
                NullLogger<ReportQueueProcessor>.Instance,
                new PathAssessmentManager(db, SystemClock.Instance)
            );
            Assert.True(
                await processor.ProcessNextAsync(settings.IngestionQueue, cancellationToken)
            );
            Assert.True(await processor.ProcessNextAsync(settings.ContentQueue, cancellationToken));
        }

        await using var verified = database.CreateDbContext();
        var reception = await verified
            .MeshPacketReceptions.Include(row => row.Packet)
            .SingleAsync(row => row.Id == receptionId, cancellationToken);
        Assert.Null(reception.HeardAt);
        Assert.NotNull(reception.Packet);
        var assessment = await verified.ReceptionPathAssessments.SingleAsync(
            row => row.ReceptionId == receptionId,
            cancellationToken
        );
        Assert.True(assessment.AdmitsNeighbourEvidence);
        using var result = JsonDocument.Parse(assessment.Result);
        Assert.Equal("FloodHistory", result.RootElement.GetProperty("PathKind").GetString());
        Assert.Equal("Empty", result.RootElement.GetProperty("Status").GetString());
        Assert.Empty(result.RootElement.GetProperty("Hops").EnumerateArray());
        Assert.Single(await verified.NeighbourObservations.ToListAsync(cancellationToken));
        Assert.Single(await verified.MeshNodes.ToListAsync(cancellationToken));
        Assert.Equal(1, database.KnowledgeReads.Count);
        var knowledgeCommand = Assert.Single(database.KnowledgeReads.Commands);
        Assert.Contains("UNION ALL", knowledgeCommand);
        Assert.Contains("reception_path_assessments", knowledgeCommand);

        // A direct replay is also a no-op, not only replay through the queue guard.
        await using (var replay = database.CreateDbContext())
        {
            await using var transaction = await replay.Database.BeginTransactionAsync(
                cancellationToken
            );
            var resultBefore = assessment.Result;
            var replayResult = await new PathAssessmentManager(
                replay,
                SystemClock.Instance
            ).AssessReceptionAsync(new(receptionId), cancellationToken);
            Assert.True(replayResult.Match(left: _ => false, right: _ => true));
            Assert.Equal(1, database.KnowledgeReads.Count);
            Assert.False(replay.ChangeTracker.HasChanges());
            Assert.Equal(
                resultBefore,
                await replay
                    .ReceptionPathAssessments.Where(row => row.ReceptionId == receptionId)
                    .Select(row => row.Result)
                    .SingleAsync(cancellationToken)
            );
        }

        await using var queueSource = NpgsqlDataSource.Create(database.ConnectionString);
        Assert.Equal(
            0L,
            await CountAsync(
                queueSource,
                $"pgmq.q_{database.Options.IngestionQueue}",
                cancellationToken
            )
        );
        Assert.Equal(
            0L,
            await CountAsync(
                queueSource,
                $"pgmq.q_{database.Options.ContentQueue}",
                cancellationToken
            )
        );
        Assert.Equal(
            1L,
            await CountAsync(
                queueSource,
                $"pgmq.a_{database.Options.IngestionQueue}",
                cancellationToken
            )
        );
        Assert.Equal(
            1L,
            await CountAsync(
                queueSource,
                $"pgmq.a_{database.Options.ContentQueue}",
                cancellationToken
            )
        );
    }

    [PgmqFact]
    public async Task Manager_requires_delivery_transaction_and_rejects_missing_packet()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await using var db = database.CreateDbContext();
        var manager = new PathAssessmentManager(db, SystemClock.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.AssessReceptionAsync(new(Guid.NewGuid()), token)
        );

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var result = await manager.AssessReceptionAsync(new(Guid.NewGuid()), token);
        Assert.True(
            result.Match(
                left: error =>
                    error is IPathAssessmentManager.AssessReceptionError.MissingReceptionPacket,
                right: _ => false
            )
        );
        Assert.Equal(0, database.KnowledgeReads.Count);
        Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [PgmqFact]
    public async Task Manager_rejects_existing_reception_without_packet_without_staging_writes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var receptionId = Guid.CreateVersion7();
        var raw = PacketCodec.Encode(PacketCodec.Create(RouteType.Direct, PacketType.Request, [1]));
        await ReceiveAsync(database, observer, raw, receptionId, DateTimeOffset.UtcNow, token);
        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var reception = await db.MeshPacketReceptions.SingleAsync(
            row => row.Id == receptionId,
            token
        );
        reception.PacketId = null;
        reception.Packet = null;
        await db.SaveChangesAsync(token);
        var knowledgeReadsBefore = database.KnowledgeReads.Count;

        var result = await new PathAssessmentManager(db, SystemClock.Instance).AssessReceptionAsync(
            new(receptionId),
            token
        );

        Assert.True(
            result.Match(
                left: error =>
                    error is IPathAssessmentManager.AssessReceptionError.MissingReceptionPacket,
                right: _ => false
            )
        );
        Assert.Equal(knowledgeReadsBefore, database.KnowledgeReads.Count);
        Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [PgmqFact]
    public async Task Manager_returns_save_failure_with_original_cause()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        var receptionId = await IngestDirectReceptionAsync(database, token);
        var cause = new DbUpdateException("synthetic-sensitive-save-details");
        await using var db = database.CreateDbContext(new FailAssessmentSave(cause));
        await using var transaction = await db.Database.BeginTransactionAsync(token);

        var result = await new PathAssessmentManager(db, SystemClock.Instance).AssessReceptionAsync(
            new(receptionId),
            token
        );

        var failure = result.Match(
            left: error =>
                error switch
                {
                    IPathAssessmentManager.AssessReceptionError.SaveFailed failed => failed,
                    _ => throw new InvalidOperationException("Expected save failure."),
                },
            right: _ => throw new InvalidOperationException("Expected save failure.")
        );
        Assert.Same(cause, failure.Cause);
        Assert.Equal("SaveFailed", failure.ToString());
        Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
    }

    [PathPgmqTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manager_propagates_unexpected_save_exceptions_and_cancellation(bool cancel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        var receptionId = await IngestDirectReceptionAsync(database, token);
        Exception cause = cancel
            ? new OperationCanceledException(token)
            : new InvalidOperationException("synthetic unexpected failure");
        await using var db = database.CreateDbContext(new FailAssessmentSave(cause));
        await using var transaction = await db.Database.BeginTransactionAsync(token);

        var thrown = await Record.ExceptionAsync(() =>
            new PathAssessmentManager(db, SystemClock.Instance).AssessReceptionAsync(
                new(receptionId),
                token
            )
        );

        Assert.Same(cause, thrown);
        Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
    }

    private static async Task<Guid> IngestDirectReceptionAsync(
        TestDatabase database,
        CancellationToken cancellationToken
    )
    {
        await SeedRegionAsync(database, cancellationToken);
        using var observer = AdvertSigningKey.Generate();
        var receptionId = Guid.CreateVersion7();
        await ReceiveAsync(
            database,
            observer,
            PacketCodec.Encode(PacketCodec.Create(RouteType.Direct, PacketType.Request, [1])),
            receptionId,
            DateTimeOffset.UtcNow,
            cancellationToken
        );
        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, cancellationToken)
        );
        return receptionId;
    }

    [PgmqFact]
    public async Task Manager_propagates_cancellation_without_staging_writes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await using var db = database.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PathAssessmentManager(db, SystemClock.Instance).AssessReceptionAsync(
                new(Guid.NewGuid()),
                cancellation.Token
            )
        );

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
    }

    [PgmqFact]
    public async Task Direct_manager_uses_injected_clock_and_propagates_decode_failure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var receptionId = Guid.CreateVersion7();
        var raw = PacketCodec.Encode(PacketCodec.Create(RouteType.Direct, PacketType.Request, [1]));
        await ReceiveAsync(database, observer, raw, receptionId, DateTimeOffset.UtcNow, token);
        await using var db = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, db)
                .ProcessNextAsync(database.Options.IngestionQueue, token)
        );
        var clock = new FixedAssessmentClock(Instant.FromUtc(2026, 11, 12, 1, 0));

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var result = await new PathAssessmentManager(db, clock).AssessReceptionAsync(
                new(receptionId),
                token
            );
            Assert.True(result.Match(left: _ => false, right: _ => true));
            Assert.Equal(
                clock.GetCurrentInstant(),
                (await db.ReceptionPathAssessments.SingleAsync(token)).AssessedAt
            );
            await transaction.RollbackAsync(token);
        }
        db.ChangeTracker.Clear();

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var reception = await db
                .MeshPacketReceptions.Include(row => row.Packet)
                .SingleAsync(row => row.Id == receptionId, token);
            reception.Packet!.RawPacket = [];
            await db.SaveChangesAsync(token);
            var knowledgeReadsBefore = database.KnowledgeReads.Count;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PathAssessmentManager(db, clock).AssessReceptionAsync(new(receptionId), token)
            );

            Assert.Contains("InvalidPacketLength", exception.Message);
            Assert.Equal(knowledgeReadsBefore, database.KnowledgeReads.Count);
            Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
        }
    }

    private class FixedAssessmentClock(Instant instant) : IClock
    {
        public Instant GetCurrentInstant() => instant;
    }

    private static async Task<long> CountAsync(
        NpgsqlDataSource source,
        string table,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.CreateCommand($"SELECT count(*) FROM {table}");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [PgmqFact]
    public async Task Independent_advert_support_selects_one_prefix_candidate_but_two_supporters_are_ambiguous()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var candidate = AdvertSigningKey.Generate();
        using var advertiser = GenerateExcludingPrefix(candidate.PublicKey[0]);
        var at = DateTimeOffset.UtcNow;

        using var catalogueObserver = AdvertSigningKey.Generate();
        await ReceiveAndProcessAsync(
            database,
            catalogueObserver,
            SignedAdvert(candidate, at),
            at,
            token
        );
        var advertiserRaw = SignedAdvert(advertiser, at);
        using var collision = GenerateWithPrefix(candidate.PublicKey[0]);
        var targetRaw = SignedAdvertWithFirstHop(
            advertiser,
            at.AddSeconds(1),
            [candidate.PublicKey[0]]
        );
        using var candidateSeedObserver = AdvertSigningKey.Generate();
        await ReceiveAndProcessAsync(
            database,
            candidateSeedObserver,
            SignedAdvert(candidate, at.AddSeconds(-2)),
            at.AddSeconds(-2),
            token
        );

        var sourceId = await ReceiveAndProcessAsync(database, observer, advertiserRaw, at, token);
        await ReceiveAndProcessAsync(
            database,
            observer,
            advertiserRaw,
            at.AddTicks(1),
            token,
            observerOverride: true
        );
        var targetId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(1),
            token
        );
        var first = await ReadAssessmentAsync(database, targetId, token);
        var hop = Assert.Single(first.RootElement.GetProperty("Hops").EnumerateArray().ToArray());
        Assert.Equal("Unresolved", hop.GetProperty("Outcome").GetString());
        Assert.Single(hop.GetProperty("Candidates").EnumerateArray());
        Assert.All(hop.GetProperty("Candidates").EnumerateArray(), item =>
            Assert.Equal("Weak", item.GetProperty("Confidence").GetString())
        );
        var originalSnapshot = await ReadSnapshotTextAsync(database, targetId, token);

        // A later catalogue collision invalidates the previously unique cached membership, but
        // historical reception JSON remains exactly as assessed.
        await ReceiveAndProcessAsync(
            database,
            candidateSeedObserver,
            SignedAdvert(collision, at),
            at,
            token
        );
        var afterCollisionId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(1).AddTicks(1),
            token
        );
        using (var afterCollision = await ReadAssessmentAsync(database, afterCollisionId, token))
        {
            var collisionHop = Assert.Single(
                afterCollision.RootElement.GetProperty("Hops").EnumerateArray().ToArray()
            );
            Assert.Equal("Ambiguous", collisionHop.GetProperty("Outcome").GetString());
            Assert.Equal(2, collisionHop.GetProperty("Candidates").GetArrayLength());
        }
        Assert.Equal(originalSnapshot, await ReadSnapshotTextAsync(database, targetId, token));

        // A second independently signed advertiser, heard by a distinct observer, supports one
        // candidate through its first-hop prefix. The target is different content, so it is eligible.
        var supportReceptionId = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvert(candidate, at.AddSeconds(2)),
            at.AddSeconds(2),
            token
        );
        var supportedId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(3),
            token
        );
        using var supported = await ReadAssessmentAsync(database, supportedId, token);
        var supportedHop = Assert.Single(
            supported.RootElement.GetProperty("Hops").EnumerateArray().ToArray()
        );
        Assert.Equal("Resolved", supportedHop.GetProperty("Outcome").GetString());
        var supportedCandidates = supportedHop.GetProperty("Candidates").EnumerateArray().ToArray();
        var selected = Assert.Single(supportedCandidates, item =>
            item.GetProperty("PublicKey").GetString()
            == Convert.ToHexStringLower(candidate.PublicKey.AsSpan())
        );
        Assert.True(selected.GetProperty("Selected").GetBoolean());
        Assert.DoesNotContain(supportedCandidates, item =>
            item.GetProperty("Selected").GetBoolean()
            && item.GetProperty("PublicKey").GetString()
                != Convert.ToHexStringLower(candidate.PublicKey.AsSpan())
        );
        Assert.Equal(
            Convert.ToHexStringLower(candidate.PublicKey.AsSpan()),
            selected.GetProperty("PublicKey").GetString()
        );
        Assert.Equal("Strong", selected.GetProperty("Confidence").GetString());
        var provenance = Assert.Single(selected.GetProperty("Supports").EnumerateArray().ToArray());
        Assert.Equal(supportReceptionId, provenance.GetProperty("SourceReceptionId").GetGuid());
        Assert.Equal("Outgoing", provenance.GetProperty("Side").GetString());

        var sameBytesLaterId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(3).AddTicks(1),
            token
        );
        using var improved = await ReadAssessmentAsync(database, sameBytesLaterId, token);
        var improvedCandidate = Assert.Single(
            Assert
                .Single(improved.RootElement.GetProperty("Hops").EnumerateArray().ToArray())
                .GetProperty("Candidates")
                .EnumerateArray()
                .ToArray(),
            item =>
                item.GetProperty("PublicKey").GetString()
                == Convert.ToHexStringLower(candidate.PublicKey.AsSpan())
        );
        Assert.Equal("Strong", improvedCandidate.GetProperty("Confidence").GetString());
        Assert.Equal(
            await ReadPacketIdAsync(database, targetId, token),
            await ReadPacketIdAsync(database, sameBytesLaterId, token)
        );
        Assert.Equal(
            await ReadContentIdAsync(database, targetId, token),
            await ReadContentIdAsync(database, sameBytesLaterId, token)
        );
        Assert.Equal(originalSnapshot, await ReadSnapshotTextAsync(database, targetId, token));

        // The signed source's own reception admits an observation, but a later packet with the
        // same payload/content cannot use that observation to resolve its own path.
        var sameContentRaw = SignedAdvertWithFirstHop(
            advertiser,
            at.AddSeconds(6),
            [candidate.PublicKey[0]]
        );
        using var isolatedObserver = AdvertSigningKey.Generate();
        var sameContentFirst = await ReceiveAndProcessAsync(
            database,
            isolatedObserver,
            sameContentRaw,
            at.AddSeconds(6),
            token
        );
        var sameContentSecond = await ReceiveAndProcessAsync(
            database,
            isolatedObserver,
            sameContentRaw,
            at.AddSeconds(6).AddTicks(1),
            token
        );
        using var sameContentResult = await ReadAssessmentAsync(database, sameContentSecond, token);
        var sameContentCandidate = Assert.Single(
            Assert
                .Single(
                    sameContentResult.RootElement.GetProperty("Hops").EnumerateArray().ToArray()
                )
                .GetProperty("Candidates")
                .EnumerateArray()
                .ToArray(),
            item =>
                item.GetProperty("PublicKey").GetString()
                == Convert.ToHexStringLower(candidate.PublicKey.AsSpan())
        );
        Assert.Equal("Weak", sameContentCandidate.GetProperty("Confidence").GetString());
        Assert.DoesNotContain(
            sameContentCandidate.GetProperty("Supports").EnumerateArray(),
            support => support.GetProperty("SourceReceptionId").GetGuid() == sameContentFirst
        );
        Assert.Contains(
            sameContentFirst,
            await ReadObservationSourceReceptionsAsync(database, token)
        );

        await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvert(collision, at.AddSeconds(4)),
            at.AddSeconds(4),
            token
        );
        var ambiguousId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(5),
            token
        );
        using var ambiguous = await ReadAssessmentAsync(database, ambiguousId, token);
        var ambiguousHop = Assert.Single(
            ambiguous.RootElement.GetProperty("Hops").EnumerateArray().ToArray()
        );
        Assert.Equal("Ambiguous", ambiguousHop.GetProperty("Outcome").GetString());
        Assert.DoesNotContain(ambiguousHop.GetProperty("Candidates").EnumerateArray(), item =>
            item.GetProperty("Selected").GetBoolean()
        );
        Assert.Contains(sourceId, await ReadObservationSourceReceptionsAsync(database, token));
    }

    [PgmqFact]
    public async Task Incoming_support_is_directed_and_selected_winners_do_not_bootstrap_adjacent_hops()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var candidate = AdvertSigningKey.Generate();
        using var advertiser = GenerateExcludingPrefix(candidate.PublicKey[0]);
        using var observer = AdvertSigningKey.Generate();
        using var helper = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;

        await ReceiveAndProcessAsync(database, helper, SignedAdvert(candidate, at), at, token);
        var sourceId = await ReceiveAndProcessAsync(
            database,
            candidate,
            SignedAdvert(advertiser, at),
            at,
            token
        );
        var targetId = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvertWithFirstHop(advertiser, at.AddSeconds(1), [candidate.PublicKey[0]]),
            at.AddSeconds(1),
            token
        );
        using (var assessment = await ReadAssessmentAsync(database, targetId, token))
        {
            var hop = Assert.Single(assessment.RootElement.GetProperty("Hops").EnumerateArray());
            var result = Assert.Single(hop.GetProperty("Candidates").EnumerateArray());
            Assert.Equal("Strong", result.GetProperty("Confidence").GetString());
            Assert.True(result.GetProperty("Selected").GetBoolean());
            var support = Assert.Single(result.GetProperty("Supports").EnumerateArray());
            Assert.Equal("Incoming", support.GetProperty("Side").GetString());
            Assert.Equal(sourceId, support.GetProperty("SourceReceptionId").GetGuid());
            Assert.Equal(
                Convert.ToHexStringLower(advertiser.PublicKey.AsSpan()),
                support.GetProperty("FromKey").GetString()
            );
            Assert.Equal(
                Convert.ToHexStringLower(candidate.PublicKey.AsSpan()),
                support.GetProperty("ToKey").GetString()
            );
        }

        // A→C cannot support C→A. This target has neither a matching incoming nor outgoing edge.
        var reverseId = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvertWithFirstHop(candidate, at.AddSeconds(2), [advertiser.PublicKey[0]]),
            at.AddSeconds(2),
            token
        );
        using (var reverse = await ReadAssessmentAsync(database, reverseId, token))
        {
            var result = Assert.Single(
                Assert
                    .Single(reverse.RootElement.GetProperty("Hops").EnumerateArray())
                    .GetProperty("Candidates")
                    .EnumerateArray()
            );
            Assert.Equal("Weak", result.GetProperty("Confidence").GetString());
            Assert.Empty(result.GetProperty("Supports").EnumerateArray());
        }

        // A now has a colliding alternative. A→C can select A at position zero using singleton C,
        // but C must not then use the selected A as an incoming anchor (the original set is ambiguous).
        using var alternative = GenerateWithPrefix(advertiser.PublicKey[0]);
        await ReceiveAndProcessAsync(
            database,
            helper,
            SignedAdvert(alternative, at.AddSeconds(3)),
            at.AddSeconds(3),
            token
        );
        var noBootstrapId = await ReceiveAndProcessAsync(
            database,
            observer,
            PacketWithDistinctPayloadPath([advertiser.PublicKey[0], candidate.PublicKey[0]]),
            at.AddSeconds(4),
            token
        );
        using var noBootstrap = await ReadAssessmentAsync(database, noBootstrapId, token);
        var hops = noBootstrap.RootElement.GetProperty("Hops").EnumerateArray().ToArray();
        Assert.Equal(2, hops.Length);
        var selected = Assert.Single(hops[0].GetProperty("Candidates").EnumerateArray(), c =>
            c.GetProperty("Selected").GetBoolean()
        );
        Assert.Equal(
            Convert.ToHexStringLower(advertiser.PublicKey.AsSpan()),
            selected.GetProperty("PublicKey").GetString()
        );
        var unpromoted = Assert.Single(hops[1].GetProperty("Candidates").EnumerateArray());
        Assert.Equal("Weak", unpromoted.GetProperty("Confidence").GetString());
        Assert.Empty(unpromoted.GetProperty("Supports").EnumerateArray());
    }

    [PgmqFact]
    public async Task Role_change_refreshes_cached_membership_and_duplicate_content_work_is_a_noop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var repeater = AdvertSigningKey.Generate();
        using var observer = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;
        var sourceId = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvert(repeater, at),
            at,
            token
        );
        var raw = PacketWithDistinctPayloadPath([repeater.PublicKey[0]]);
        var originalId = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            at.AddSeconds(1),
            token
        );
        var originalJson = await ReadSnapshotTextAsync(database, originalId, token);
        var packetId = await ReadPacketIdAsync(database, originalId, token);
        string fingerprint;
        await using (var db = database.CreateDbContext())
        {
            var step = await db.PathSteps.SingleAsync(token);
            fingerprint = step.CandidateCatalogueFingerprint!;
            Assert.Single(step.CandidateNodeIds!);
        }

        await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvert(repeater, at.AddSeconds(2), NodeRole.Client),
            at.AddSeconds(2),
            token
        );
        var laterId = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            at.AddSeconds(3),
            token
        );
        using (var later = await ReadAssessmentAsync(database, laterId, token))
        {
            var hop = Assert.Single(later.RootElement.GetProperty("Hops").EnumerateArray());
            Assert.Equal("Unknown", hop.GetProperty("Outcome").GetString());
            Assert.Empty(hop.GetProperty("Candidates").EnumerateArray());
        }
        Assert.Equal(packetId, await ReadPacketIdAsync(database, laterId, token));
        Assert.Equal(originalJson, await ReadSnapshotTextAsync(database, originalId, token));
        await using var verified = database.CreateDbContext();
        var refreshed = await verified.PathSteps.SingleAsync(token);
        Assert.NotEqual(fingerprint, refreshed.CandidateCatalogueFingerprint);
        Assert.Empty(refreshed.CandidateNodeIds!);

        var snapshots = await verified.ReceptionPathAssessments.CountAsync(token);
        var observations = await verified.NeighbourObservations.CountAsync(token);
        var modifiedAt = await verified.MeshNodes.Select(n => n.ModifiedAt).SingleAsync(token);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var queue = new NpgmqClient(source);
        var archives = await CountAsync(source, $"pgmq.a_{database.Options.ContentQueue}", token);
        // Re-deliver completed ADVERT work. The throwing decorator proves the assessment/effects path
        // is not entered again; successful archival is required, not a swallowed retry.
        await queue.SendAsync(
            database.Options.ContentQueue,
            new ContentWork(sourceId),
            new Dictionary<string, object> { ["x-pgmq-group"] = "completed-advert-replay" },
            token
        );
        await using (var replay = database.CreateDbContext())
        {
            Assert.True(
                await CreateProcessor(database, replay, failAfterAssessment: true)
                    .ProcessNextAsync(database.Options.ContentQueue, token)
            );
        }
        Assert.Equal(
            0L,
            await CountAsync(source, $"pgmq.q_{database.Options.ContentQueue}", token)
        );
        Assert.Equal(
            archives + 1,
            await CountAsync(source, $"pgmq.a_{database.Options.ContentQueue}", token)
        );
        Assert.Equal(snapshots, await verified.ReceptionPathAssessments.CountAsync(token));
        Assert.Equal(observations, await verified.NeighbourObservations.CountAsync(token));
        Assert.Equal(
            modifiedAt,
            await verified.MeshNodes.Select(n => n.ModifiedAt).SingleAsync(token)
        );
        Assert.Equal(originalJson, await ReadSnapshotTextAsync(database, originalId, token));
    }

    [PgmqFact]
    public async Task Prefix_edge_is_invalidated_by_new_collision_without_rewriting_prior_snapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var finalObserver = AdvertSigningKey.Generate();
        using var observerPrefixAnchor = GenerateExcludingPrefix(finalObserver.PublicKey[0]);
        using var firstForwarder = GenerateExcludingPrefix(finalObserver.PublicKey[0]);
        using var advertiser = GenerateExcludingPrefix(
            firstForwarder.PublicKey[0],
            finalObserver.PublicKey[0]
        );
        using var prefixCollision = GenerateWithPrefix(finalObserver.PublicKey[0]);
        using var collisionObserver = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;

        await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvert(firstForwarder, at),
            at,
            token
        );
        await ReceiveAndProcessAsync(
            database,
            observerPrefixAnchor,
            SignedAdvert(finalObserver, at),
            at.AddTicks(1),
            token
        );
        await ReceiveAndProcessAsync(
            database,
            observerPrefixAnchor,
            SignedAdvert(firstForwarder, at),
            at.AddTicks(1),
            token
        );
        var supportId = await ReceiveAndProcessAsync(
            database,
            observer,
            SignedAdvertWithFirstHop(
                firstForwarder,
                at.AddSeconds(1),
                [finalObserver.PublicKey[0]]
            ),
            at.AddSeconds(1),
            token
        );
        var targetRaw = SignedAdvertWithFirstHop(
            advertiser,
            at.AddSeconds(2),
            [firstForwarder.PublicKey[0]]
        );
        var earlierId = await ReceiveAndProcessAsync(
            database,
            finalObserver,
            targetRaw,
            at.AddSeconds(2),
            token
        );
        using var earlier = await ReadAssessmentAsync(database, earlierId, token);
        var earlierHops = earlier.RootElement.GetProperty("Hops").EnumerateArray().ToArray();
        Assert.Single(earlierHops);
        var earlierCandidates = earlierHops[0].GetProperty("Candidates").EnumerateArray().ToArray();
        var earlierCandidate = Assert.Single(earlierCandidates, candidate =>
            candidate.GetProperty("PublicKey").GetString()
            == Convert.ToHexStringLower(firstForwarder.PublicKey.AsSpan())
        );
        Assert.Equal("Strong", earlierCandidate.GetProperty("Confidence").GetString());
        Assert.Contains(earlierCandidate.GetProperty("Supports").EnumerateArray(), support =>
            support.GetProperty("SourceReceptionId").GetGuid() == supportId
        );
        var earlierJson = await ReadSnapshotTextAsync(database, earlierId, token);

        await ReceiveAndProcessAsync(
            database,
            collisionObserver,
            SignedAdvert(prefixCollision, at.AddSeconds(3)),
            at.AddSeconds(3),
            token
        );
        var laterId = await ReceiveAndProcessAsync(
            database,
            finalObserver,
            targetRaw,
            at.AddSeconds(4),
            token
        );
        using var later = await ReadAssessmentAsync(database, laterId, token);
        var laterHop = Assert.Single(
            later.RootElement.GetProperty("Hops").EnumerateArray().ToArray()
        );
        Assert.Equal("Unresolved", laterHop.GetProperty("Outcome").GetString());
        Assert.DoesNotContain(laterHop.GetProperty("Candidates").EnumerateArray(), candidate =>
            candidate.GetProperty("Confidence").GetString() == "Strong"
        );
        Assert.Equal(earlierJson, await ReadSnapshotTextAsync(database, earlierId, token));
    }

    [PathPgmqTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Flood_path_widths_one_through_three_are_snapshotted(int width)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var raw = PacketCodec.Encode(
            PacketCodec.Create(
                RouteType.Flood,
                PacketType.GroupText,
                [1, 2],
                new byte[width],
                width
            )
        );
        var receptionId = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            DateTimeOffset.UtcNow,
            token
        );
        using var result = await ReadAssessmentAsync(database, receptionId, token);
        Assert.Equal("FloodHistory", result.RootElement.GetProperty("PathKind").GetString());
        Assert.Equal("Unresolved", result.RootElement.GetProperty("Status").GetString());
        var hop = Assert.Single(result.RootElement.GetProperty("Hops").EnumerateArray().ToArray());
        Assert.Equal(width, hop.GetProperty("Width").GetInt32());
        Assert.Equal(new string('0', width * 2), hop.GetProperty("Prefix").GetString());
        Assert.Equal("Unknown", hop.GetProperty("Outcome").GetString());
    }

    [PathPgmqTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Trie_shares_identical_prefixes_but_keeps_distinct_ancestry(int width)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var common = Enumerable.Repeat((byte)0x5a, width).ToArray();
        byte[] firstPath = [.. common, .. Enumerable.Repeat((byte)0x11, width)];
        byte[] secondPath = [.. common, .. Enumerable.Repeat((byte)0x22, width)];
        var sharedPayload = new byte[] { 0x51, 0x52 };
        var firstId = await ReceiveAndProcessAsync(
            database,
            observer,
            PacketCodec.Encode(
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.GroupText,
                    sharedPayload,
                    firstPath,
                    width
                )
            ),
            DateTimeOffset.UtcNow,
            token
        );
        var secondId = await ReceiveAndProcessAsync(
            database,
            observer,
            PacketCodec.Encode(
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.GroupText,
                    sharedPayload,
                    secondPath,
                    width
                )
            ),
            DateTimeOffset.UtcNow,
            token
        );
        await using var db = database.CreateDbContext();
        var packetIds = await db
            .MeshPacketReceptions.Where(row => row.Id == firstId || row.Id == secondId)
            .Select(row => row.PacketId!.Value)
            .ToArrayAsync(token);
        var hops = await db
            .PathHops.Where(hop => packetIds.Contains(hop.PacketId))
            .ToArrayAsync(token);
        var firstFirstHop = Assert.Single(hops, hop =>
            hop.PacketId == packetIds[0] && hop.Position == 0
        );
        var secondFirstHop = Assert.Single(hops, hop =>
            hop.PacketId == packetIds[1] && hop.Position == 0
        );
        Assert.Equal(firstFirstHop.PathStepId, secondFirstHop.PathStepId);
        var firstSecondHop = Assert.Single(hops, hop =>
            hop.PacketId == packetIds[0] && hop.Position == 1
        );
        var secondSecondHop = Assert.Single(hops, hop =>
            hop.PacketId == packetIds[1] && hop.Position == 1
        );
        Assert.NotEqual(firstSecondHop.PathStepId, secondSecondHop.PathStepId);

        // The same encoded prefix at a repeated position or after different ancestry is not
        // the same trie step. Direction and zero bytes survive unchanged at every width.
        byte[] loopPath = [.. common, .. new byte[width], .. common];
        byte[] reversePath = [.. Enumerable.Repeat((byte)0x11, width), .. common];
        var loopId = await ReceiveAndProcessAsync(
            database,
            observer,
            PacketCodec.Encode(
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.GroupText,
                    sharedPayload,
                    loopPath,
                    width
                )
            ),
            DateTimeOffset.UtcNow,
            token
        );
        var reverseId = await ReceiveAndProcessAsync(
            database,
            observer,
            PacketCodec.Encode(
                PacketCodec.Create(
                    RouteType.Flood,
                    PacketType.GroupText,
                    sharedPayload,
                    reversePath,
                    width
                )
            ),
            DateTimeOffset.UtcNow,
            token
        );
        var loopPacketId = await ReadPacketIdAsync(database, loopId, token);
        var reversePacketId = await ReadPacketIdAsync(database, reverseId, token);
        var loopHops = await db
            .PathHops.Where(h => h.PacketId == loopPacketId)
            .OrderBy(h => h.Position)
            .ToArrayAsync(token);
        var reverseHops = await db
            .PathHops.Where(h => h.PacketId == reversePacketId)
            .OrderBy(h => h.Position)
            .ToArrayAsync(token);
        Assert.Equal(3, loopHops.Length);
        Assert.Equal(2, reverseHops.Length);
        Assert.Equal(firstFirstHop.PathStepId, loopHops[0].PathStepId);
        Assert.NotEqual(loopHops[0].PathStepId, loopHops[2].PathStepId);
        Assert.NotEqual(loopHops[0].PathStepId, reverseHops[1].PathStepId);
        Assert.NotEqual(firstSecondHop.PathStepId, reverseHops[0].PathStepId);
        var zeroStep = await db.PathSteps.SingleAsync(s => s.Id == loopHops[1].PathStepId, token);
        Assert.Equal(new byte[width], zeroStep.Prefix);
        Assert.Equal(loopHops[0].PathStepId, zeroStep.ParentStepId);
    }

    [PathPgmqTheory]
    [InlineData(
        RouteType.Direct,
        PacketType.GroupText,
        "UnsupportedDirectAssessment",
        "DirectRemainingRoute"
    )]
    [InlineData(
        RouteType.TransportDirect,
        PacketType.GroupText,
        "UnsupportedDirectAssessment",
        "DirectRemainingRoute"
    )]
    [InlineData(RouteType.Flood, PacketType.Trace, "UnsupportedTraceAssessment", "TraceProgress")]
    [InlineData(RouteType.Direct, PacketType.Trace, "UnsupportedTraceAssessment", "TraceProgress")]
    [InlineData(RouteType.TransportFlood, PacketType.Path, "Empty", "FloodHistory")]
    public async Task Unsupported_direct_and_trace_packets_are_explicitly_snapshotted(
        RouteType route,
        PacketType type,
        string expectedStatus,
        string expectedKind
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        byte[] transportCodes = route is RouteType.TransportFlood or RouteType.TransportDirect
            ? [1, 2, 3, 4]
            : [];
        var raw = PacketCodec.Encode(
            PacketCodec.Create(route, type, [1, 2, 3], transportCodes: transportCodes)
        );
        var receptionId = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            DateTimeOffset.UtcNow,
            token
        );
        using var result = await ReadAssessmentAsync(database, receptionId, token);
        Assert.Equal(expectedStatus, result.RootElement.GetProperty("Status").GetString());
        Assert.Equal(expectedKind, result.RootElement.GetProperty("PathKind").GetString());
        Assert.Empty(result.RootElement.GetProperty("Hops").EnumerateArray());
    }

    [PgmqFact]
    public async Task Evidence_waiting_ahead_in_content_queue_is_not_used_until_its_assessment_commits()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        using var candidate = AdvertSigningKey.Generate();
        var at = DateTimeOffset.UtcNow;
        using var catalogueObserver = AdvertSigningKey.Generate();
        await ReceiveAndProcessAsync(
            database,
            catalogueObserver,
            SignedAdvert(candidate, at),
            at,
            token
        );

        await using var db = database.CreateDbContext();
        var processor = CreateProcessor(database, db);
        var targetId = Guid.CreateVersion7();
        var targetRaw = PacketWithDistinctPayloadPath([candidate.PublicKey[0]]);
        await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
            new(
                targetId,
                [
                    .. ObserverReports.Packet(
                        new(Convert.ToHexStringLower(observer.PublicKey.AsSpan()), "obs"),
                        Convert.ToHexStringLower(targetRaw),
                        at.AddSeconds(1)
                    ),
                ],
                $"meshcore/TST/{Convert.ToHexStringLower(observer.PublicKey.AsSpan())}/packets",
                "ws://synthetic.invalid/mqtt",
                at.AddSeconds(1)
            ),
            token
        );
        Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));

        var supportId = Guid.CreateVersion7();
        await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
            new(
                supportId,
                [
                    .. ObserverReports.Packet(
                        new(Convert.ToHexStringLower(observer.PublicKey.AsSpan()), "obs"),
                        Convert.ToHexStringLower(SignedAdvert(candidate, at.AddSeconds(2))),
                        at
                    ),
                ],
                $"meshcore/TST/{Convert.ToHexStringLower(observer.PublicKey.AsSpan())}/packets",
                "ws://synthetic.invalid/mqtt",
                at
            ),
            token
        );
        Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));

        // The evidence-producing ADVERT is queued behind the target's content work.
        await using var queueSource = NpgsqlDataSource.Create(database.ConnectionString);
        var queue = new NpgmqClient(queueSource);
        var targetHead = await queue.ReadGroupedHeadAsync<string>(
            database.Options.ContentQueue,
            0,
            1,
            token
        );
        Assert.NotEmpty(targetHead);
        Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
        using (var target = await ReadAssessmentAsync(database, targetId, token))
        {
            var candidateResults = Assert
                .Single(target.RootElement.GetProperty("Hops").EnumerateArray().ToArray())
                .GetProperty("Candidates")
                .EnumerateArray()
                .ToArray();
            var candidateResult = Assert.Single(candidateResults, item =>
                item.GetProperty("PublicKey").GetString()
                == Convert.ToHexStringLower(candidate.PublicKey.AsSpan())
            );
            Assert.Equal("Weak", candidateResult.GetProperty("Confidence").GetString());
        }
        Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
        Assert.True(
            await db.ReceptionPathAssessments.AnyAsync(row => row.ReceptionId == supportId, token)
        );
        var laterId = await ReceiveAndProcessAsync(
            database,
            observer,
            targetRaw,
            at.AddSeconds(3),
            token
        );
        using var later = await ReadAssessmentAsync(database, laterId, token);
        var supported = Assert.Single(
            Assert
                .Single(later.RootElement.GetProperty("Hops").EnumerateArray())
                .GetProperty("Candidates")
                .EnumerateArray()
        );
        Assert.Equal("Strong", supported.GetProperty("Confidence").GetString());
        Assert.Equal(
            supportId,
            Assert
                .Single(supported.GetProperty("Supports").EnumerateArray())
                .GetProperty("SourceReceptionId")
                .GetGuid()
        );
    }

    [PgmqFact]
    public async Task Save_failure_retries_then_quarantines_with_safe_reason_and_rolls_back_effects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var advertiser = AdvertSigningKey.Generate();
        using var observer = AdvertSigningKey.Generate();
        var receptionId = Guid.CreateVersion7();
        await ReceiveAsync(
            database,
            observer,
            SignedAdvert(advertiser, DateTimeOffset.UtcNow),
            receptionId,
            DateTimeOffset.UtcNow,
            token
        );
        var interceptor = new FailAssessmentSave(
            new DbUpdateException("synthetic-sensitive-save-details")
        );
        await using var db = database.CreateDbContext(interceptor);
        var processor = CreateProcessor(database, db);
        Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);

        for (var attempt = 1; attempt <= database.Options.MaximumAttempts; attempt++)
        {
            Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
            Assert.Empty(await db.NeighbourObservations.ToArrayAsync(token));
            Assert.Empty(await db.MeshNodes.ToArrayAsync(token));
            var exhausted = attempt == database.Options.MaximumAttempts;
            Assert.Equal(
                exhausted ? 0L : 1L,
                await CountAsync(source, $"pgmq.q_{database.Options.ContentQueue}", token)
            );
            Assert.Equal(
                exhausted ? 1L : 0L,
                await CountAsync(source, $"pgmq.a_{database.Options.ContentQueue}", token)
            );
        }

        var queue = new NpgmqClient(source);
        var quarantined = await queue.ReadAsync<QuarantinedReport>(
            database.Options.QuarantineQueue,
            0,
            token
        );
        Assert.NotNull(quarantined);
        Assert.Equal("PathAssessmentSaveFailed", quarantined.Message!.Reason);
        Assert.Equal(database.Options.MaximumAttempts, interceptor.Attempts);
    }

    [PgmqFact]
    public async Task Failure_after_assessment_rolls_back_snapshot_observation_and_queue_archival()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var advertiser = AdvertSigningKey.Generate();
        using var observer = AdvertSigningKey.Generate();
        var raw = SignedAdvert(advertiser, DateTimeOffset.UtcNow);
        var receptionId = Guid.CreateVersion7();
        await ReceiveAsync(database, observer, raw, receptionId, DateTimeOffset.UtcNow, token);

        await using (var db = database.CreateDbContext())
        {
            var processor = CreateProcessor(database, db, failAfterAssessment: true);
            Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));
            Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
        }
        await using var verified = database.CreateDbContext();
        Assert.False(
            await verified.ReceptionPathAssessments.AnyAsync(
                row => row.ReceptionId == receptionId,
                token
            )
        );
        Assert.False(
            await verified.NeighbourObservations.AnyAsync(
                row => row.SourceReceptionId == receptionId,
                token
            )
        );
        Assert.False(
            await verified.MeshNodes.AnyAsync(
                node => node.PublicKey == advertiser.PublicKey.ToArray(),
                token
            )
        );
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        Assert.Equal(
            1L,
            await CountAsync(source, $"pgmq.q_{database.Options.ContentQueue}", token)
        );
        Assert.Equal(
            0L,
            await CountAsync(source, $"pgmq.a_{database.Options.ContentQueue}", token)
        );

        await using var retry = database.CreateDbContext();
        var success = CreateProcessor(database, retry);
        Assert.True(await success.ProcessNextAsync(database.Options.ContentQueue, token));
        Assert.True(
            await retry.ReceptionPathAssessments.AnyAsync(
                row => row.ReceptionId == receptionId,
                token
            )
        );
        Assert.True(
            await retry.NeighbourObservations.AnyAsync(
                row => row.SourceReceptionId == receptionId,
                token
            )
        );
        Assert.False(await success.ProcessNextAsync(database.Options.ContentQueue, token));
        Assert.Single(await retry.ReceptionPathAssessments.ToListAsync(token));
    }

    [PgmqFact]
    public async Task Cancellation_after_assessment_rolls_back_and_leaves_content_work_retryable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var advertiser = AdvertSigningKey.Generate();
        using var observer = AdvertSigningKey.Generate();
        var receptionId = Guid.CreateVersion7();
        await ReceiveAsync(
            database,
            observer,
            SignedAdvert(advertiser, DateTimeOffset.UtcNow),
            receptionId,
            DateTimeOffset.UtcNow,
            token
        );

        using var cancellation = new CancellationTokenSource();
        await using (var db = database.CreateDbContext())
        {
            var processor = CreateProcessor(database, db, cancelAfterAssessment: cancellation);
            Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                processor.ProcessNextAsync(database.Options.ContentQueue, cancellation.Token)
            );
        }
        await using var verified = database.CreateDbContext();
        Assert.False(
            await verified.ReceptionPathAssessments.AnyAsync(
                row => row.ReceptionId == receptionId,
                token
            )
        );
        Assert.False(
            await verified.NeighbourObservations.AnyAsync(
                row => row.SourceReceptionId == receptionId,
                token
            )
        );
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        Assert.Equal(
            1L,
            await CountAsync(source, $"pgmq.q_{database.Options.ContentQueue}", token)
        );
        Assert.Equal(
            0L,
            await CountAsync(source, $"pgmq.a_{database.Options.ContentQueue}", token)
        );
        Assert.Empty(await verified.MeshNodes.ToArrayAsync(token));
        await using var retry = database.CreateDbContext();
        Assert.True(
            await CreateProcessor(database, retry)
                .ProcessNextAsync(database.Options.ContentQueue, token)
        );
        Assert.Single(await retry.ReceptionPathAssessments.ToArrayAsync(token));
        Assert.Single(await retry.NeighbourObservations.ToArrayAsync(token));
    }

    [PathPgmqTheory]
    [InlineData(0x15, 0xC1, "UnsupportedWidth")]
    [InlineData(0x95, 0x01, "UnsupportedVersion")]
    public async Task Unsupported_width_and_version_are_snapshotted_without_hops(
        byte header,
        byte encodedPathLength,
        string expected
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        await SeedRegionAsync(database, token);
        using var observer = AdvertSigningKey.Generate();
        var raw =
            encodedPathLength == 0xC1
                ? new byte[] { header, encodedPathLength, 0x11, 0x22, 0x33, 0x44, 0x42 }
                : new byte[] { header, encodedPathLength, 0x11, 0x42 };
        var receptionId = await ReceiveAndProcessAsync(
            database,
            observer,
            raw,
            DateTimeOffset.UtcNow,
            token
        );
        using var result = await ReadAssessmentAsync(database, receptionId, token);
        Assert.Equal(expected, result.RootElement.GetProperty("Status").GetString());
        Assert.Empty(result.RootElement.GetProperty("Hops").EnumerateArray());
        await using var verified = database.CreateDbContext();
        Assert.Equal(raw, (await verified.MeshPackets.SingleAsync(token)).RawPacket);
        Assert.Empty(await verified.PathHops.ToArrayAsync(token));
        Assert.Empty(await verified.NeighbourObservations.ToArrayAsync(token));
    }

    [PgmqFact]
    public async Task Generated_branching_reports_share_path_steps_without_leaking_observer_support()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(token);
        var fixture = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "network",
            "branching.json"
        );
        var inventory = NetworkInventory.Load(fixture);
        using var generator = NetworkPacketGenerator.Load(inventory);
        var adapter = new NetworkReportAdapter(generator.Observers);
        await SeedObserverRegionsAsync(
            database,
            token,
            generator.Observers.Select(observer => observer.Configuration.Region).Distinct()
        );

        // Catalogue identities through a synthetic observation point without learning any
        // repeater-to-repeater or repeater-to-real-observer links.
        var helperConfiguration = inventory.Nodes.Single(node => node.Id == "O2").Observer;
        using var metadata = NetworkPacketGenerator.Load(
            inventory with
            {
                Nodes =
                [
                    .. inventory.Nodes.Select(node =>
                        node.Id switch
                        {
                            "helper" => node with { Observer = helperConfiguration },
                            _ => node with { Neighbours = [new("helper", 1)] },
                        }
                    ),
                ],
            }
        );
        var metadataAdapter = new NetworkReportAdapter(metadata.Observers);
        foreach (var id in new[] { "A2", "A3", "A4", "A5" })
        {
            var hearing = Assert.Single(metadata.Execute(id, Instant.FromUtc(2026, 11, 18, 10, 0)));
            await SubmitHearingAsync(database, metadataAdapter, hearing, token);
        }

        var direct = Assert.Single(
            generator.Execute("A2", Instant.FromUtc(2026, 11, 18, 10, 1)),
            hearing => hearing.ObserverId == "O2"
        );
        await SubmitHearingAsync(database, adapter, direct, token);

        var probes = new Dictionary<string, Guid>();
        var probePathCounts = new Dictionary<Guid, int>();
        var probePacketIds = new Dictionary<Guid, Guid>();
        foreach (var width in new[] { 1, 2, 3 })
        {
            foreach (
                var hearing in generator.Execute(
                    "client",
                    Instant.FromUtc(2026, 11, 18, 10, 2 + width),
                    width
                )
            )
            {
                var id = await SubmitHearingAsync(database, adapter, hearing, token);
                probes[
                    $"{width}:{hearing.ObserverId}:{Convert.ToHexStringLower(hearing.Raw.AsSpan())}"
                ] = id;
                Assert.True(PacketCodec.TryDecode(hearing.Raw.AsSpan(), out var packet));
                probePathCounts[id] = packet.PathCount;
                await using var identityDb = database.CreateDbContext();
                probePacketIds[id] = await identityDb
                    .MeshPacketReceptions.Where(row => row.Id == id)
                    .Select(row => row.PacketId!.Value)
                    .SingleAsync(token);
            }
        }

        foreach (var (probe, receptionId) in probes)
        {
            using var assessment = await ReadAssessmentAsync(database, receptionId, token);
            var hops = assessment.RootElement.GetProperty("Hops").EnumerateArray().ToArray();
            Assert.Equal(probePathCounts[receptionId], hops.Length);
            Assert.Empty(hops[0].GetProperty("Candidates").EnumerateArray()); // A1 is not catalogued.
            for (var position = 1; position < hops.Length; position++)
            {
                var candidates = hops[position]
                    .GetProperty("Candidates")
                    .EnumerateArray()
                    .ToArray();
                Assert.Single(candidates);
                var directO2Support = probe.Split(':')[1] == "O2" && position == hops.Length - 1;
                Assert.All(candidates, candidate =>
                {
                    Assert.Equal(
                        directO2Support ? "Strong" : "Weak",
                        candidate.GetProperty("Confidence").GetString()
                    );
                    Assert.Equal(directO2Support, candidate.GetProperty("Selected").GetBoolean());
                });
            }
        }

        await using var verified = database.CreateDbContext();
        Assert.Equal(30, await verified.PathHops.CountAsync(token));
        var probeIds = probePacketIds.Values.ToArray();
        var packets = await verified
            .MeshPackets.Where(packet => probeIds.Contains(packet.Id))
            .ToArrayAsync(token);
        Assert.Equal(9, packets.Length);
        var contentGroups = packets.GroupBy(packet => packet.ContentId).ToArray();
        Assert.Equal(3, contentGroups.Length);
        foreach (var group in contentGroups)
        {
            var packetIds = group.Select(p => p.Id).ToArray();
            Assert.Equal(3, packetIds.Length);
            var occurrences = await verified
                .PathHops.Where(h => packetIds.Contains(h.PacketId))
                .ToArrayAsync(token);
            Assert.Equal(10, occurrences.Length);
            Assert.Equal(
                new[] { 2, 4, 4 },
                occurrences.GroupBy(h => h.PacketId).Select(g => g.Count()).Order()
            );
            Assert.Equal(5, occurrences.Select(h => h.PathStepId).Distinct().Count());
            var steps = await verified
                .PathSteps.Where(s => s.ContentId == group.Key)
                .ToArrayAsync(token);
            Assert.Equal(5, steps.Length);
            foreach (var position in new[] { 0, 1, 2 })
            {
                var atPosition = occurrences.Where(h => h.Position == position).ToArray();
                Assert.Equal(position == 2 ? 2 : 3, atPosition.Length);
                Assert.Single(atPosition.Select(h => h.PathStepId).Distinct());
            }
            var tails = occurrences.Where(h => h.Position == 3).ToArray();
            Assert.Equal(2, tails.Length);
            Assert.Equal(2, tails.Select(h => h.PathStepId).Distinct().Count());
            Assert.Single(
                steps
                    .Where(s => tails.Select(h => h.PathStepId).Contains(s.Id))
                    .Select(s => s.ParentStepId)
                    .Distinct()
            );
            Assert.All(steps, step =>
                Assert.False(string.IsNullOrEmpty(step.CandidateCatalogueFingerprint))
            );
            Assert.Empty(Assert.Single(steps, s => s.Position == 0).CandidateNodeIds!);
            Assert.All(steps.Where(s => s.Position > 0), step =>
                Assert.Single(step.CandidateNodeIds!)
            );
        }
    }

    private static async Task<Guid> SubmitHearingAsync(
        TestDatabase database,
        NetworkReportAdapter adapter,
        ObserverHearing hearing,
        CancellationToken token
    )
    {
        var receptionId = Guid.CreateVersion7();
        var arrivedAt = hearing.HeardAt.ToDateTimeOffset().AddSeconds(1);
        await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
            adapter.ToInput(hearing, arrivedAt, receptionId),
            token
        );
        await using var db = database.CreateDbContext();
        var processor = CreateProcessor(database, db);
        Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));
        Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
        return receptionId;
    }

    private static async Task SeedObserverRegionsAsync(
        TestDatabase database,
        CancellationToken token,
        IEnumerable<string> regions
    )
    {
        await using var db = database.CreateDbContext();
        foreach (var region in regions)
        {
            db.Regions.Add(
                new StoredRegion
                {
                    Id = Guid.CreateVersion7(),
                    Code = region,
                    CountryCode = "LT",
                }
            );
        }
        await db.SaveChangesAsync(token);
    }

    private static byte[] SignedAdvert(
        AdvertSigningKey key,
        DateTimeOffset at,
        NodeRole role = NodeRole.Repeater
    ) =>
        AdvertAppDataCodec
            .Encode(new(role, Name: "synthetic"))
            .SelectLeft(error => error.ToString())
            .Select(appData =>
                AdvertCodec.CreateSigned(key, checked((uint)at.ToUnixTimeSeconds()), appData)
            )
            .SelectMany(advert => AdvertCodec.Encode(advert).SelectLeft(error => error.ToString()))
            .Select(payload =>
                PacketCodec.Encode(PacketCodec.Create(RouteType.Flood, PacketType.Advert, payload))
            )
            .Match(
                left: error => throw new InvalidOperationException(error),
                right: bytes => bytes
            );

    private static AdvertSigningKey GenerateWithPrefix(byte prefix)
    {
        while (true)
        {
            var key = AdvertSigningKey.Generate();
            if (key.PublicKey[0] == prefix)
            {
                return key;
            }
            key.Dispose();
        }
    }

    private static AdvertSigningKey GenerateExcludingPrefix(params byte[] prefixes)
    {
        while (true)
        {
            var key = AdvertSigningKey.Generate();
            if (!prefixes.Contains(key.PublicKey[0]))
            {
                return key;
            }
            key.Dispose();
        }
    }

    private static byte[] SignedAdvertWithFirstHop(
        AdvertSigningKey key,
        DateTimeOffset at,
        byte[] path
    )
    {
        var packet = SignedAdvert(key, at);
        Assert.True(PacketCodec.TryDecode(packet, out var decoded));
        return PacketCodec.Encode(
            PacketCodec.Create(
                RouteType.Flood,
                PacketType.Advert,
                decoded.Payload.AsSpan(),
                path,
                1
            )
        );
    }

    private static byte[] PacketWithDistinctPayloadPath(byte[] path) =>
        PacketCodec.Encode(
            PacketCodec.Create(
                RouteType.Flood,
                PacketType.GroupText,
                Guid.NewGuid().ToByteArray(),
                path,
                1
            )
        );

    private static async Task SeedRegionAsync(TestDatabase database, CancellationToken token)
    {
        await using var db = database.CreateDbContext();
        db.Regions.Add(
            new StoredRegion
            {
                Id = Guid.CreateVersion7(),
                Code = "TST",
                CountryCode = "LT",
            }
        );
        await db.SaveChangesAsync(token);
    }

    private static async Task<Guid> ReceiveAndProcessAsync(
        TestDatabase database,
        AdvertSigningKey observer,
        byte[] raw,
        DateTimeOffset at,
        CancellationToken token,
        bool observerOverride = false
    )
    {
        var receptionId = Guid.CreateVersion7();
        var observerKey = observerOverride ? AdvertSigningKey.Generate() : null;
        try
        {
            var key = observerKey ?? observer;
            var hex = Convert.ToHexStringLower(key.PublicKey.AsSpan());
            await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
                new(
                    receptionId,
                    [
                        .. ObserverReports.Packet(
                            new(hex, "synthetic"),
                            Convert.ToHexStringLower(raw),
                            at
                        ),
                    ],
                    $"meshcore/TST/{hex}/packets",
                    "ws://synthetic.invalid/mqtt",
                    at
                ),
                token
            );
            await using var db = database.CreateDbContext();
            var processor = CreateProcessor(database, db);
            Assert.True(await processor.ProcessNextAsync(database.Options.IngestionQueue, token));
            Assert.True(await processor.ProcessNextAsync(database.Options.ContentQueue, token));
            return receptionId;
        }
        finally
        {
            observerKey?.Dispose();
        }
    }

    private static ReportQueueProcessor CreateProcessor(
        TestDatabase database,
        TrackingDbContext db,
        bool failAfterAssessment = false,
        CancellationTokenSource? cancelAfterAssessment = null
    ) =>
        new(
            db,
            new PacketProcessingManager(),
            new ReportIngestionManager(db, SystemClock.Instance),
            new AdvertNodeManager(db, SystemClock.Instance),
            new GroupTextManager(
                db,
                PacketConfiguration.Read(new ConfigurationBuilder().Build()),
                SystemClock.Instance
            ),
            Options.Create(database.Options),
            NullLogger<ReportQueueProcessor>.Instance,
            cancelAfterAssessment is not null
                    ? new CancelAfterAssessment(
                        new PathAssessmentManager(db, SystemClock.Instance),
                        cancelAfterAssessment
                    )
                : failAfterAssessment
                    ? new FailingAfterAssessment(
                        new PathAssessmentManager(db, SystemClock.Instance)
                    )
                : new PathAssessmentManager(db, SystemClock.Instance)
        );

    private static async Task ReceiveAsync(
        TestDatabase database,
        AdvertSigningKey observer,
        byte[] raw,
        Guid receptionId,
        DateTimeOffset at,
        CancellationToken token
    )
    {
        var hex = Convert.ToHexStringLower(observer.PublicKey.AsSpan());
        await new ReportReceiver(database, Options.Create(database.Options)).ReceiveReportAsync(
            new(
                receptionId,
                [
                    .. ObserverReports.Packet(
                        new(hex, "synthetic"),
                        Convert.ToHexStringLower(raw),
                        at
                    ),
                ],
                $"meshcore/TST/{hex}/packets",
                "ws://synthetic.invalid/mqtt",
                at
            ),
            token
        );
    }

    private class FailingAfterAssessment(IPathAssessmentManager inner) : IPathAssessmentManager
    {
        public async Task<
            Either<IPathAssessmentManager.AssessReceptionError, Unit>
        > AssessReceptionAsync(
            IPathAssessmentManager.AssessReceptionInput input,
            CancellationToken cancellationToken
        )
        {
            await inner.AssessReceptionAsync(input, cancellationToken);
            throw new InvalidOperationException("fail after assessment write");
        }
    }

    private class CancelAfterAssessment(
        IPathAssessmentManager inner,
        CancellationTokenSource cancellation
    ) : IPathAssessmentManager
    {
        public async Task<
            Either<IPathAssessmentManager.AssessReceptionError, Unit>
        > AssessReceptionAsync(
            IPathAssessmentManager.AssessReceptionInput input,
            CancellationToken cancellationToken
        )
        {
            var result = await inner.AssessReceptionAsync(input, cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    private static async Task<JsonDocument> ReadAssessmentAsync(
        TestDatabase database,
        Guid receptionId,
        CancellationToken token
    )
    {
        await using var db = database.CreateDbContext();
        var result = await db
            .ReceptionPathAssessments.Where(row => row.ReceptionId == receptionId)
            .Select(row => row.Result)
            .SingleAsync(token);
        return JsonDocument.Parse(result);
    }

    private static async Task<string> ReadSnapshotTextAsync(
        TestDatabase database,
        Guid receptionId,
        CancellationToken token
    )
    {
        await using var db = database.CreateDbContext();
        return await db
            .ReceptionPathAssessments.Where(row => row.ReceptionId == receptionId)
            .Select(row => row.Result)
            .SingleAsync(token);
    }

    private static async Task<Guid> ReadPacketIdAsync(
        TestDatabase database,
        Guid receptionId,
        CancellationToken token
    )
    {
        await using var db = database.CreateDbContext();
        return await db
            .MeshPacketReceptions.Where(row => row.Id == receptionId)
            .Select(row => row.PacketId!.Value)
            .SingleAsync(token);
    }

    private static async Task<Guid> ReadContentIdAsync(
        TestDatabase database,
        Guid receptionId,
        CancellationToken token
    )
    {
        await using var db = database.CreateDbContext();
        return await db
            .MeshPacketReceptions.Where(row => row.Id == receptionId)
            .Select(row => row.Packet!.ContentId)
            .SingleAsync(token);
    }

    private static async Task<Guid[]> ReadObservationSourceReceptionsAsync(
        TestDatabase database,
        CancellationToken token
    )
    {
        await using var db = database.CreateDbContext();
        return await db
            .NeighbourObservations.Select(row => row.SourceReceptionId)
            .ToArrayAsync(token);
    }

    private class TestDatabase(string connectionString, string adminConnectionString, string name)
        : IDbContextFactory<TrackingDbContext>,
            IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;
        public KnowledgeReadInterceptor KnowledgeReads { get; } = new();
        public ReportDeliveryOptions Options { get; } =
            new()
            {
                IngestionQueue = "path_in_" + Guid.NewGuid().ToString("N"),
                ContentQueue = "path_content_" + Guid.NewGuid().ToString("N"),
                QuarantineQueue = "path_failed_" + Guid.NewGuid().ToString("N"),
                RetryDelaySeconds = 0,
            };

        public TrackingDbContext CreateDbContext() => CreateDbContext([]);

        public TrackingDbContext CreateDbContext(params IInterceptor[] interceptors) =>
            new(
                TrackingDatabase
                    .Configure(new DbContextOptionsBuilder<TrackingDbContext>(), ConnectionString)
                    .AddInterceptors(KnowledgeReads)
                    .AddInterceptors(interceptors)
                    .Options
            );

        public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken)
        {
            var admin = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = int.Parse(Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT")!),
                Username = "pgmq_test",
                Database = "postgres",
                Pooling = false,
                Timeout = 5,
                CommandTimeout = 15,
            };
            var name = "path_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
            var adminConnectionString = admin.ConnectionString;
            admin.Database = name;
            var database = new TestDatabase(admin.ConnectionString, adminConnectionString, name);
            try
            {
                await using var db = database.CreateDbContext();
                await db.Database.MigrateAsync(cancellationToken);
                await db.Database.OpenConnectionAsync(cancellationToken);
                var queue = new NpgmqClient((NpgsqlConnection)db.Database.GetDbConnection());
                await queue.CreateQueueAsync(database.Options.IngestionQueue, cancellationToken);
                await queue.CreateQueueAsync(database.Options.ContentQueue, cancellationToken);
                await queue.CreateFifoIndexAsync(database.Options.ContentQueue, cancellationToken);
                await queue.CreateQueueAsync(database.Options.QuarantineQueue, cancellationToken);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(timeout.Token);
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE {name} WITH (FORCE)",
                connection
            );
            await drop.ExecuteNonQueryAsync(timeout.Token);
        }
    }

    private class KnowledgeReadInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public int Count => Commands.Count;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains("neighbour_observations", StringComparison.Ordinal)
                && command.CommandText.Contains("node_information", StringComparison.Ordinal)
            )
            {
                Commands.Add(command.CommandText);
            }
            return ValueTask.FromResult(result);
        }
    }

    private class FailAssessmentSave(Exception cause) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                eventData
                    .Context!.ChangeTracker.Entries<ReceptionPathAssessment>()
                    .Any(entry => entry.State == EntityState.Added)
            )
            {
                Attempts++;
                throw cause;
            }
            return ValueTask.FromResult(result);
        }
    }

    private class PathPgmqTheoryAttribute : TheoryAttribute
    {
        public PathPgmqTheoryAttribute()
        {
            if (Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT") is null)
            {
                Skip = "Requires the explicitly provisioned disposable PostgreSQL instance.";
            }
        }
    }
}
