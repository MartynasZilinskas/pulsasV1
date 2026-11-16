using System.Text.Json;
using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Npgmq;
using Npgsql;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.Persistence;
using Pulsas.Persistence.Ingestion;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Receptions;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;
using static Pulsas.Persistence.Ingestion.IReportIngestionManager;
using static Pulsas.Persistence.Nodes.IAdvertNodeManager;

namespace Pulsas.Worker.Tests;

public partial class AdvertIntegrationTestsDelivery : IAsyncLifetime
{
    private readonly ReportDeliveryOptions settings = new()
    {
        IngestionQueue = "test_in_" + Guid.NewGuid().ToString("N"),
        ContentQueue = "test_content_" + Guid.NewGuid().ToString("N"),
        QuarantineQueue = "test_failed_" + Guid.NewGuid().ToString("N"),
        MaximumAttempts = 2,
        RetryDelaySeconds = 0,
    };

    public async Task InitializeAsync()
    {
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        await database.Database.OpenConnectionAsync();
        var queue = new NpgmqClient((NpgsqlConnection)database.Database.GetDbConnection());
        foreach (
            var name in new[]
            {
                settings.IngestionQueue,
                settings.ContentQueue,
                settings.QuarantineQueue,
            }
        )
        {
            await queue.CreateQueueAsync(name);
        }
        await queue.CreateFifoIndexAsync(settings.ContentQueue);
    }

    public async Task DisposeAsync()
    {
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        await database.Database.OpenConnectionAsync();
        var queue = new NpgmqClient((NpgsqlConnection)database.Database.GetDbConnection());
        foreach (
            var name in new[]
            {
                settings.IngestionQueue,
                settings.ContentQueue,
                settings.QuarantineQueue,
            }
        )
        {
            await queue.DropQueueAsync(name);
        }
    }

    private class DeliveryDatabaseFactory : IDbContextFactory<TrackingDbContext>
    {
        public TrackingDbContext CreateDbContext() =>
            new(
                TrackingDatabase
                    .Configure(
                        new DbContextOptionsBuilder<TrackingDbContext>(),
                        Environment.GetEnvironmentVariable("PULSAS_GEOGRAPHY_TEST_CONNECTION")!
                    )
                    .Options
            );
    }

    private class ReportScenario : IAsyncDisposable
    {
        private readonly AdvertSigningKey observerKey = AdvertSigningKey.Generate();
        private readonly List<byte[]> nodeKeys = [];
        public ObserverIdentity Observer { get; }
        public string Broker { get; } = $"ws://test-{Guid.NewGuid():N}.invalid/mqtt";
        public Guid[] Receptions { get; }

        public byte[] ObserverBytes => observerKey.PublicKey.ToArray();

        public ReportScenario(int count, params AdvertSigningKey[] keys)
        {
            Observer = new(Convert.ToHexStringLower(observerKey.PublicKey.AsSpan()), "test");
            Receptions = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            nodeKeys.AddRange(keys.Select(key => key.PublicKey.ToArray()));
        }

        public Task ReceiveAsync(
            ReportDeliveryOptions options,
            ReadOnlyMemory<byte> report,
            DateTimeOffset arrivedAt,
            int index = 0,
            string? topic = null
        ) =>
            new ReportReceiver(
                new DeliveryDatabaseFactory(),
                Options.Create(options)
            ).ReceiveReportAsync(
                new(
                    Receptions[index],
                    [.. report.ToArray()],
                    topic ?? $"meshcore/KUN/{Observer.PublicKey}/packets",
                    Broker,
                    arrivedAt
                ),
                CancellationToken.None
            );

        public async ValueTask DisposeAsync()
        {
            try
            {
                await AdvertIntegrationTestsDelivery.CleanAsync(
                    Receptions,
                    Broker,
                    ObserverBytes,
                    nodeKeys
                );
            }
            finally
            {
                observerKey.Dispose();
            }
        }
    }

    [GeographyFact]
    public async Task Receiver_durably_accepts_original_report_without_inline_processing()
    {
        var databases = new DeliveryDatabaseFactory();
        var receptionId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        await new ReportReceiver(databases, Options.Create(settings)).ReceiveReportAsync(
            new(
                receptionId,
                [1, 2, 3],
                "meshcore/VNO/test/packets",
                "ws://synthetic.invalid/mqtt",
                at
            ),
            CancellationToken.None
        );
        // A fresh context/connection can observe the report after the receiver has gone away.
        await using var database = databases.CreateDbContext();
        await database.Database.OpenConnectionAsync();
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"SELECT count(*) FROM pgmq.q_{settings.IngestionQueue} WHERE message ->> 'ReceptionId' = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = receptionId.ToString();
        command.Parameters.Add(parameter);
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        Assert.False(await database.MeshPacketReceptions.AnyAsync(row => row.Id == receptionId));
    }

    private ReportQueueProcessor Processor(
        TrackingDbContext database,
        IAdvertNodeManager? nodes = null,
        IReportIngestionManager? ingestion = null,
        Pulsas.Worker.ApiService.Configuration.PacketConfiguration? packetConfiguration = null,
        Pulsas.Worker.ApiService.Chat.IGroupTextManager? groupText = null
    ) =>
        new(
            database,
            new PacketProcessingManager(),
            ingestion ?? new ReportIngestionManager(database, SystemClock.Instance),
            nodes ?? new AdvertNodeManager(database, SystemClock.Instance),
            groupText ?? new Pulsas.Worker.ApiService.Chat.GroupTextManager(
                database,
                packetConfiguration ?? EmptyPacketConfiguration(),
                SystemClock.Instance
            ),
            Options.Create(settings),
            NullLogger<ReportQueueProcessor>.Instance
        );

    private async Task<bool> Process(
        string queue,
        Func<TrackingDbContext, IAdvertNodeManager>? nodes = null,
        Func<TrackingDbContext, IReportIngestionManager>? ingestion = null,
        Pulsas.Worker.ApiService.Configuration.PacketConfiguration? packetConfiguration = null,
        Func<TrackingDbContext, Pulsas.Worker.ApiService.Chat.IGroupTextManager>? groupText = null
    )
    {
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        queue = queue == ReportQueues.Ingestion ? settings.IngestionQueue : settings.ContentQueue;
        return await Processor(
                database,
                nodes?.Invoke(database),
                ingestion?.Invoke(database),
                packetConfiguration,
                groupText?.Invoke(database)
            )
            .ProcessNextAsync(queue, CancellationToken.None);
    }

    private class FailingAfterWrite(IAdvertNodeManager inner) : IAdvertNodeManager
    {
        public async Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
            ProcessAdvertInput input,
            CancellationToken cancellationToken
        )
        {
            await inner.ProcessAdvertAsync(input, cancellationToken);
            throw new InvalidOperationException("synthetic failure after node write");
        }
    }

    private class BlockingAfterWrite(
        IAdvertNodeManager inner,
        TaskCompletionSource entered,
        Task release
    ) : IAdvertNodeManager
    {
        public async Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
            ProcessAdvertInput input,
            CancellationToken cancellationToken
        )
        {
            var result = await inner.ProcessAdvertAsync(input, cancellationToken);
            entered.TrySetResult();
            await release.WaitAsync(cancellationToken);
            return result;
        }
    }

    private class ConcurrentIngestion(
        IReportIngestionManager inner,
        TaskCompletionSource ready,
        Action arrived
    ) : IReportIngestionManager
    {
        public async Task<Either<IngestReportError, MeshPacketReception>> IngestReportAsync(
            IngestReportInput input,
            CancellationToken cancellationToken
        )
        {
            arrived();
            await ready.Task.WaitAsync(cancellationToken);
            return await inner.IngestReportAsync(input, cancellationToken);
        }
    }

    [GeographyFact]
    public async Task Concurrent_reports_share_packet_content_and_keep_distinct_receptions()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(2, key);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var ids = scenario.Receptions;
        var at = DateTimeOffset.UtcNow;
        var raw = ObserverReports.CreateAdvert(key, 1, at);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        for (var index = 0; index < ids.Length; index++)
        {
            await scenario.ReceiveAsync(
                settings,
                ObserverReports.Packet(observer, raw, at),
                at,
                index
            );
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        await using var first = new DeliveryDatabaseFactory().CreateDbContext();
        await using var second = new DeliveryDatabaseFactory().CreateDbContext();
        Task<bool> Attempt(TrackingDbContext database) =>
            Processor(
                    database,
                    ingestion: new ConcurrentIngestion(
                        new ReportIngestionManager(database, SystemClock.Instance),
                        ready,
                        () =>
                        {
                            if (Interlocked.Increment(ref count) == 2)
                            {
                                ready.TrySetResult();
                            }
                        }
                    )
                )
                .ProcessNextAsync(settings.IngestionQueue, timeout.Token);
        Assert.All(await Task.WhenAll(Attempt(first), Attempt(second)), Assert.True);
        // A classified creation conflict remains queued for a fresh-context attempt.
        for (
            var retry = 0;
            retry < settings.MaximumAttempts && await Process(ReportQueues.Ingestion);
            retry++
        ) { }
        Assert.False(await Process(ReportQueues.Ingestion));
        await using var check = new DeliveryDatabaseFactory().CreateDbContext();
        var rows = await check
            .MeshPacketReceptions.Where(row => ids.Contains(row.Id))
            .Include(row => row.Packet)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows.Select(row => row.PacketId).Distinct());
        Assert.Single(rows.Select(row => row.Packet!.ContentId).Distinct());
        Assert.True(await Process(ReportQueues.Content));
        Assert.True(await Process(ReportQueues.Content));
        Assert.False(await Process(ReportQueues.Content));
        var queue = await QueueAsync(check);
        Assert.Empty(await queue.ReadBatchAsync<string>(settings.QuarantineQueue));
    }

    [GeographyFact]
    public async Task Grouped_content_blocks_same_content_but_allows_other_content_to_commit()
    {
        using var key = AdvertSigningKey.Generate();
        using var otherKey = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(3, key, otherKey);
        var observer = scenario.Observer;
        var ids = scenario.Receptions;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? blocked = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var at = DateTimeOffset.UtcNow;
            for (var index = 0; index < ids.Length; index++)
            {
                var raw = ObserverReports.CreateAdvert(index < 2 ? key : otherKey, 1, at);
                await scenario.ReceiveAsync(
                    settings,
                    ObserverReports.Packet(observer, raw, at),
                    at,
                    index
                );
                Assert.True(await Process(ReportQueues.Ingestion));
            }
            await using var database = new DeliveryDatabaseFactory().CreateDbContext();
            blocked = Processor(
                    database,
                    new BlockingAfterWrite(
                        new AdvertNodeManager(database, SystemClock.Instance),
                        entered,
                        release.Task
                    )
                )
                .ProcessNextAsync(settings.ContentQueue, timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            Assert.True(await Process(ReportQueues.Content));
            await using (var check = new DeliveryDatabaseFactory().CreateDbContext())
            {
                Assert.True(
                    await check.MeshNodes.AnyAsync(node =>
                        node.PublicKey == otherKey.PublicKey.ToArray()
                    )
                );
                Assert.False(
                    await check.MeshNodes.AnyAsync(node =>
                        node.PublicKey == key.PublicKey.ToArray()
                    )
                );
            }
            Assert.False(await Process(ReportQueues.Content));
            release.SetResult();
            Assert.True(await blocked);
            Assert.True(await Process(ReportQueues.Content));
            Assert.False(await Process(ReportQueues.Content));
        }
        finally
        {
            release.TrySetResult();
            if (blocked is not null)
            {
                await blocked;
            }
        }
    }

    [GeographyFact]
    public async Task Canceled_content_attempt_rolls_back_node_and_claim_then_can_resume()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(1, key);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var id = scenario.Receptions[0];
        var at = DateTimeOffset.UtcNow;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await scenario.ReceiveAsync(
                settings,
                ObserverReports.Packet(observer, ObserverReports.CreateAdvert(key, 1, at), at),
                at
            );
            Assert.True(await Process(ReportQueues.Ingestion));
            await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var handling = Processor(
                        database,
                        new BlockingAfterWrite(
                            new AdvertNodeManager(database, SystemClock.Instance),
                            entered,
                            release.Task
                        )
                    )
                    .ProcessNextAsync(settings.ContentQueue, cancellation.Token);
                await entered.Task.WaitAsync(cancellation.Token);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handling);
            }
            await using (var check = new DeliveryDatabaseFactory().CreateDbContext())
            {
                Assert.False(
                    await check.MeshNodes.AnyAsync(node =>
                        node.PublicKey == key.PublicKey.ToArray()
                    )
                );
                await check.Database.OpenConnectionAsync();
                await using var command = check.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT read_ct FROM pgmq.q_{settings.ContentQueue}";
                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
            Assert.True(await Process(ReportQueues.Content));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private class FailingIngestionAfterWrite(IReportIngestionManager inner)
        : IReportIngestionManager
    {
        public async Task<Either<IngestReportError, MeshPacketReception>> IngestReportAsync(
            IngestReportInput input,
            CancellationToken cancellationToken
        )
        {
            var result = await inner.IngestReportAsync(input, cancellationToken);
            if (result.Match(left: _ => false, right: _ => true))
            {
                throw new InvalidOperationException("synthetic failure after ingestion writes");
            }
            return result;
        }
    }

    private static async Task<NpgmqClient> QueueAsync(TrackingDbContext database)
    {
        await database.Database.OpenConnectionAsync();
        return new NpgmqClient((NpgsqlConnection)database.Database.GetDbConnection());
    }

    [GeographyTheory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Invalid_queue_envelopes_are_audited_without_blocking_following_work(
        string envelope
    )
    {
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        var queue = await QueueAsync(database);
        var id = await queue.SendAsync(settings.IngestionQueue, envelope);
        Assert.True(await Process(ReportQueues.Ingestion));
        Assert.False(await Process(ReportQueues.Ingestion));
        var quarantined = await queue.ReadAsync<QuarantinedReport>(settings.QuarantineQueue);
        Assert.NotNull(quarantined);
        Assert.Equal(id, quarantined.Message!.MessageId);
        Assert.Equal(1, quarantined.Message.Attempts);
        Assert.Equal("InvalidQueueEnvelope", quarantined.Message.Reason);
        Assert.Equal(envelope, quarantined.Message.OriginalEnvelope);
    }

    [GeographyTheory]
    [InlineData("MalformedJson")]
    [InlineData("ExpectedObject")]
    public async Task Malformed_reports_are_quarantined_without_fabricating_receptions(
        string reason
    )
    {
        await using var scenario = new ReportScenario(1);
        var report = reason == "MalformedJson" ? "{"u8.ToArray() : "[]"u8.ToArray();
        await scenario.ReceiveAsync(settings, report, DateTimeOffset.UtcNow);
        Assert.True(await Process(ReportQueues.Ingestion));
        Assert.False(await Process(ReportQueues.Ingestion));
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        Assert.False(
            await database.MeshPacketReceptions.AnyAsync(row => row.Id == scenario.Receptions[0])
        );
        var queue = await QueueAsync(database);
        var quarantine = Assert.Single(
            await queue.ReadBatchAsync<QuarantinedReport>(settings.QuarantineQueue)
        );
        Assert.Contains(reason, quarantine.Message!.Reason);
    }

    [GeographyTheory]
    [InlineData("meshcore/invalid/invalid/packets", "InvalidTopic")]
    [InlineData(
        "meshcore/KUN/0000000000000000000000000000000000000000000000000000000000000000/packets",
        "ObserverMismatch"
    )]
    public async Task Invalid_topic_and_observer_are_quarantined_before_following_valid_report(
        string topic,
        string expectedReason
    )
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(2, key);
        var at = DateTimeOffset.UtcNow;
        at = at.AddTicks(-(at.Ticks % 10)); // PostgreSQL timestamps retain microseconds.
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(scenario.Observer, "", at),
            at,
            topic: topic
        );
        Assert.True(await Process(ReportQueues.Ingestion));
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(
                scenario.Observer,
                ObserverReports.CreateAdvert(key, 1, at),
                at.AddMinutes(-10)
            ),
            at,
            index: 1
        );
        Assert.True(await Process(ReportQueues.Ingestion));
        Assert.True(await Process(ReportQueues.Content));
        Assert.False(await Process(ReportQueues.Ingestion));
        Assert.False(await Process(ReportQueues.Content));
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        Assert.False(
            await database.MeshPacketReceptions.AnyAsync(row => row.Id == scenario.Receptions[0])
        );
        var accepted = await database.MeshPacketReceptions.SingleAsync(row =>
            row.Id == scenario.Receptions[1]
        );
        Assert.Equal(Instant.FromDateTimeOffset(at), accepted.ArrivedAt);
        Assert.Equal(
            "synthetic-1",
            (
                await database.MeshNodes.SingleAsync(node =>
                    node.PublicKey == key.PublicKey.ToArray()
                )
            ).Name
        );
        var queue = await QueueAsync(database);
        var quarantine = Assert.Single(
            await queue.ReadBatchAsync<QuarantinedReport>(settings.QuarantineQueue)
        );
        Assert.Contains(expectedReason, quarantine.Message!.Reason);
    }

    [GeographyFact]
    public async Task Retry_attempts_survive_rollback_and_exhaustion_quarantines_original_work()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(1, key);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var reception = scenario.Receptions[0];
        var at = DateTimeOffset.UtcNow;
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(observer, ObserverReports.CreateAdvert(key, 1, at), at),
            at
        );
        Assert.True(await Process(ReportQueues.Ingestion));
        for (var attempt = 1; attempt <= settings.MaximumAttempts; attempt++)
        {
            Assert.True(
                await Process(ReportQueues.Content, database => new FailingAfterWrite(
                    new AdvertNodeManager(database, SystemClock.Instance)
                ))
            );
            await using var check = new DeliveryDatabaseFactory().CreateDbContext();
            Assert.False(
                await check.MeshNodes.AnyAsync(node => node.PublicKey == key.PublicKey.ToArray())
            );
        }
        Assert.False(await Process(ReportQueues.Content));
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        var queue = await QueueAsync(database);
        var quarantine = Assert.Single(
            await queue.ReadBatchAsync<QuarantinedReport>(settings.QuarantineQueue)
        );
        Assert.Equal(2, quarantine.Message!.Attempts);
        Assert.Equal("InvalidOperationException", quarantine.Message.Reason);
        Assert.Equal(
            reception,
            JsonSerializer
                .Deserialize<ContentWork>(quarantine.Message.OriginalEnvelope)!
                .ReceptionId
        );
    }

    [GeographyTheory]
    [InlineData("", "absent", false)]
    [InlineData("not-hex", "invalid_hex", false)]
    [InlineData("01", "malformed:", true)]
    public async Task Undecodable_packets_preserve_reception_evidence(
        string raw,
        string reason,
        bool hasRaw
    )
    {
        await using var scenario = new ReportScenario(1);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var reception = scenario.Receptions[0];
        var at = DateTimeOffset.UtcNow;
        await scenario.ReceiveAsync(settings, ObserverReports.Packet(observer, raw, at), at);
        Assert.True(await Process(ReportQueues.Ingestion));
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        var stored = await database.MeshPacketReceptions.SingleAsync(row => row.Id == reception);
        Assert.Null(stored.PacketId);
        Assert.Null(stored.HeardAt);
        Assert.Equal(hasRaw, stored.RawPacket is not null);
        Assert.StartsWith(reason, stored.DecodeError);
        Assert.False(await Process(ReportQueues.Content));
    }

    [GeographyTheory]
    [InlineData("")]
    [InlineData("82")]
    [InlineData("82ff")]
    [InlineData("02ff")]
    public async Task Verified_advert_and_appdata_bytes_survive_queue_ingestion(string appDataHex)
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(1, key);
        var at = DateTimeOffset.UtcNow;
        var advert = AdvertCodec.CreateSigned(
            key,
            checked((uint)at.ToUnixTimeSeconds()),
            Convert.FromHexString(appDataHex)
        );
        var encoded = Assert.IsType<byte[]>(
            AdvertCodec.Encode(advert).Match<object>(left: error => error, right: bytes => bytes)
        );
        var raw = Convert.ToHexStringLower(
            PacketCodec.Encode(PacketCodec.Create(RouteType.Flood, PacketType.Advert, encoded))
        );
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(scenario.Observer, raw, at),
            at
        );
        Assert.True(await Process(ReportQueues.Ingestion));
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            var reception = await database
                .MeshPacketReceptions.Include(row => row.Packet)
                    .ThenInclude(packet => packet!.Content)
                .SingleAsync(row => row.Id == scenario.Receptions[0]);
            var payload = Assert.IsType<AdvertPayload>(
                AdvertCodec
                    .Decode(reception.Packet!.Content.Payload)
                    .Match<object>(left: error => error, right: value => value)
            );
            Assert.Equal(advert.PublicKey.ToArray(), payload.PublicKey.ToArray());
            Assert.Equal(advert.Timestamp, payload.Timestamp);
            Assert.Equal(advert.Signature.ToArray(), payload.Signature.ToArray());
            Assert.Equal(advert.AppData.ToArray(), payload.AppData.ToArray());
        }
        Assert.True(await Process(ReportQueues.Content));
        await using var projected = new DeliveryDatabaseFactory().CreateDbContext();
        var node = await projected.MeshNodes.SingleAsync(item =>
            item.PublicKey == key.PublicKey.ToArray()
        );
        Assert.Equal(
            appDataHex == "82" ? (long?)advert.Timestamp : null,
            node.AdvertMetadataTimestamp
        );
    }

    [GeographyFact]
    public async Task Ingestion_failure_after_writes_rolls_back_and_retry_creates_one_reception()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(1, key);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var reception = scenario.Receptions[0];
        var at = DateTimeOffset.UtcNow;
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(observer, ObserverReports.CreateAdvert(key, 1, at), at),
            at
        );
        Assert.True(
            await Process(
                ReportQueues.Ingestion,
                ingestion: database => new FailingIngestionAfterWrite(
                    new ReportIngestionManager(database, SystemClock.Instance)
                )
            )
        );
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            Assert.False(await database.MeshPacketReceptions.AnyAsync(row => row.Id == reception));
        }

        Assert.True(await Process(ReportQueues.Ingestion));
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            var row = await database.MeshPacketReceptions.SingleAsync(row => row.Id == reception);
            Assert.NotNull(row.PacketId);
            Assert.Equal(
                1,
                await database.MeshPacketReceptions.CountAsync(row => row.Id == reception)
            );
        }
        Assert.True(await Process(ReportQueues.Content));
    }

    [GeographyFact]
    public async Task Queue_pipeline_preserves_arrival_and_rolls_back_content_side_effects()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(2, key);
        var observer = scenario.Observer;
        var broker = scenario.Broker;
        var first = scenario.Receptions[0];
        var second = scenario.Receptions[1];
        var at = DateTimeOffset.UtcNow;
        at = at.AddTicks(-(at.Ticks % 10)); // PostgreSQL timestamps retain microseconds.
        var raw = ObserverReports.CreateAdvert(key, 1, at.AddSeconds(301));
        await scenario.ReceiveAsync(settings, ObserverReports.Packet(observer, raw, at), at);
        await scenario.ReceiveAsync(
            settings,
            ObserverReports.Packet(observer, raw, at),
            at.AddSeconds(2),
            1
        );
        Assert.True(await Process(ReportQueues.Ingestion));
        Assert.True(await Process(ReportQueues.Ingestion));
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            var receptions = await database
                .MeshPacketReceptions.Where(row => row.Id == first || row.Id == second)
                .Include(row => row.Packet)
                .ToListAsync();
            Assert.Equal(2, receptions.Count);
            Assert.Single(receptions.Select(row => row.PacketId).Distinct());
            Assert.Equal(
                Instant.FromDateTimeOffset(at),
                receptions.Single(row => row.Id == first).ArrivedAt
            );
            Assert.Equal(
                Instant.FromDateTimeOffset(at.AddSeconds(2)),
                receptions.Single(row => row.Id == second).ArrivedAt
            );
            Assert.All(receptions, row => Assert.Null(row.HeardAt));
        }
        Assert.True(await Process(ReportQueues.Content));
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            Assert.Null(
                (
                    await database.MeshNodes.SingleAsync(node =>
                        node.PublicKey == key.PublicKey.ToArray()
                    )
                ).AdvertMetadataTimestamp
            );
        }
        Assert.True(
            await Process(ReportQueues.Content, database => new FailingAfterWrite(
                new AdvertNodeManager(database, SystemClock.Instance)
            ))
        );
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            Assert.Null(
                (
                    await database.MeshNodes.SingleAsync(node =>
                        node.PublicKey == key.PublicKey.ToArray()
                    )
                ).AdvertMetadataTimestamp
            );
        }
        Assert.True(await Process(ReportQueues.Content));
        await using (var database = new DeliveryDatabaseFactory().CreateDbContext())
        {
            Assert.Equal(
                "synthetic-1",
                (
                    await database.MeshNodes.SingleAsync(node =>
                        node.PublicKey == key.PublicKey.ToArray()
                    )
                ).Name
            );
            Assert.Equal(
                2,
                await database.MeshPacketReceptions.CountAsync(row =>
                    row.Id == first || row.Id == second
                )
            );
        }
        Assert.False(await Process(ReportQueues.Content));
    }

    [GeographyFact]
    public async Task Queue_projects_generator_adverts_into_each_real_region()
    {
        using var key = AdvertSigningKey.Generate();
        await using var scenario = new ReportScenario(4, key);
        var regions = new[] { "KUN", "PLQ", "SQQ", "VNO" };
        var at = DateTimeOffset.UtcNow;
        for (var round = 1; round <= regions.Length; round++)
        {
            var receivedAt = at.AddSeconds(round);
            var report = ObserverReports.Packet(
                scenario.Observer,
                ObserverReports.CreateAdvert(key, round, receivedAt),
                receivedAt
            );
            await scenario.ReceiveAsync(settings, report, receivedAt, round - 1);
            Assert.True(await Process(ReportQueues.Ingestion));
            Assert.True(await Process(ReportQueues.Content));
            await using var database = new DeliveryDatabaseFactory().CreateDbContext();
            var node = await database.MeshNodes.SingleAsync(item =>
                item.PublicKey == key.PublicKey.ToArray()
            );
            Assert.Equal(
                regions[round - 1],
                (await database.Regions.SingleAsync(item => item.Id == node.LocationRegionId)).Code
            );
            Assert.Equal($"synthetic-{round}", node.Name);
            Assert.Equal(NodeRole.Repeater, node.Role);
            Assert.Equal(MeshNodeSourceType.ObservedAdvert, node.SourceType);
            Assert.Equal((long)receivedAt.ToUnixTimeSeconds(), node.AdvertMetadataTimestamp);
            Assert.Null(node.LatestAdvertPacketId);
            Assert.Null(node.LatestAdvertHeardAt);
        }
    }

    private static async Task CleanAsync(
        Guid[] receptions,
        string broker,
        byte[] observer,
        IReadOnlyCollection<byte[]> nodes
    )
    {
        await using var database = new DeliveryDatabaseFactory().CreateDbContext();
        var packetIds = await database
            .MeshPacketReceptions.Where(row => receptions.Contains(row.Id))
            .Select(row => row.PacketId)
            .ToListAsync();
        var contentIds = await database
            .MeshPackets.Where(row => packetIds.Contains(row.Id))
            .Select(row => row.ContentId)
            .ToListAsync();
        await database
            .MeshPacketReceptions.Where(row => receptions.Contains(row.Id))
            .ExecuteDeleteAsync();
        await database.MeshPackets.Where(row => packetIds.Contains(row.Id)).ExecuteDeleteAsync();
        await database
            .PacketContents.Where(row => contentIds.Contains(row.Id))
            .ExecuteDeleteAsync();
        foreach (var node in nodes)
        {
            await database.MeshNodes.Where(row => row.PublicKey == node).ExecuteDeleteAsync();
        }
        await database.Observers.Where(row => row.PublicKey == observer).ExecuteDeleteAsync();
        await database.Brokers.Where(row => row.Endpoint == broker).ExecuteDeleteAsync();
    }
}
