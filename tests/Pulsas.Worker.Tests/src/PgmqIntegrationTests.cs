using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgmq;
using Npgsql;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.Persistence;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Regions;
using Xunit;

namespace Pulsas.Worker.Tests;

// Opt-in boundary spike: real PGMQ + production EF mappings, not a wired ingestion operation.
public class PgmqIntegrationTests
{
    [PgmqFact]
    public async Task Locked_content_head_blocks_overtaking_even_with_expired_visibility()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var producer = new NpgmqClient(source);
        var a1 = await SendAsync(producer, "A", "A1", cancellationToken);
        var a2 = await SendAsync(producer, "A", "A2", cancellationToken);
        var b1 = await SendAsync(producer, "B", "B1", cancellationToken);

        await using var first = database.CreateContext();
        await first.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await first.Database.BeginTransactionAsync(cancellationToken);
        var firstClient = new NpgmqClient((NpgsqlConnection)first.Database.GetDbConnection());
        // Zero timeout makes the lease immediately expired: only the transaction lock protects A.
        var claimed = Assert.Single(
            await firstClient.ReadGroupedHeadAsync<string>(
                "ingestion",
                vt: 0,
                limit: 1,
                cancellationToken
            )
        );
        Assert.Equal(a1, claimed.MsgId);

        await using (var other = database.CreateContext())
        {
            await other.Database.OpenConnectionAsync(cancellationToken);
            await using var otherTransaction = await other.Database.BeginTransactionAsync(
                cancellationToken
            );
            var otherClient = new NpgmqClient((NpgsqlConnection)other.Database.GetDbConnection());
            var independent = Assert.Single(
                await otherClient.ReadGroupedHeadAsync<string>(
                    "ingestion",
                    vt: 0,
                    limit: 10,
                    cancellationToken
                )
            );
            Assert.Equal(b1, independent.MsgId);
            Assert.True(await otherClient.ArchiveAsync("ingestion", b1, cancellationToken));
            await otherTransaction.CommitAsync(cancellationToken);
        }

        Assert.Empty(
            await producer.ReadGroupedHeadAsync<string>(
                "ingestion",
                vt: 0,
                limit: 10,
                cancellationToken
            )
        );
        Assert.True(await firstClient.ArchiveAsync("ingestion", a1, cancellationToken));
        // Even archive-before-commit must not expose A2.
        Assert.Empty(
            await producer.ReadGroupedHeadAsync<string>(
                "ingestion",
                vt: 0,
                limit: 10,
                cancellationToken
            )
        );
        await transaction.CommitAsync(cancellationToken);
        var next = Assert.Single(
            await producer.ReadGroupedHeadAsync<string>(
                "ingestion",
                vt: 0,
                limit: 10,
                cancellationToken
            )
        );
        Assert.Equal(a2, next.MsgId);
        Assert.Equal("\"A2\"", next.Message);
    }

    [PgmqFact]
    public async Task EF_rows_and_queue_completion_commit_or_roll_back_together()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var producer = new NpgmqClient(source);
        var messageId = await SendAsync(producer, "A", "A1", cancellationToken);
        var now = Instant.FromUtc(2026, 11, 14, 10, 0);
        var receptionId = Guid.CreateVersion7();

        foreach (var commit in new[] { false, true })
        {
            await using (var db = database.CreateContext())
            {
                await db.Database.OpenConnectionAsync(cancellationToken);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                var client = new NpgmqClient((NpgsqlConnection)db.Database.GetDbConnection());
                var message = Assert.Single(
                    await client.ReadGroupedHeadAsync<string>(
                        "ingestion",
                        vt: 0,
                        limit: 1,
                        cancellationToken
                    )
                );
                Assert.Equal(messageId, message.MsgId);
                AddReception(db, receptionId, now);
                await db.SaveChangesAsync(cancellationToken);
                Assert.True(await client.ArchiveAsync("ingestion", messageId, cancellationToken));
                // A different connection must see neither uncommitted EF writes nor queue completion.
                await using var outside = database.CreateContext();
                Assert.False(await outside.MeshPacketReceptions.AnyAsync(cancellationToken));
                Assert.Equal(1L, await CountAsync(source, "pgmq.q_ingestion", cancellationToken));
                Assert.Equal(0L, await CountAsync(source, "pgmq.a_ingestion", cancellationToken));
                if (commit)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }

            await using var verified = database.CreateContext();
            Assert.Equal(
                commit ? 1 : 0,
                await verified.MeshPacketReceptions.CountAsync(cancellationToken)
            );
            Assert.Equal(commit ? 1 : 0, await verified.MeshPackets.CountAsync(cancellationToken));
            Assert.Equal(
                commit ? 1 : 0,
                await verified.PacketContents.CountAsync(cancellationToken)
            );
            Assert.Equal(
                commit ? 0L : 1L,
                await CountAsync(source, "pgmq.q_ingestion", cancellationToken)
            );
            Assert.Equal(
                commit ? 1L : 0L,
                await CountAsync(source, "pgmq.a_ingestion", cancellationToken)
            );
            if (commit)
            {
                var reception = await verified
                    .MeshPacketReceptions.Include(r => r.Packet)
                    .SingleAsync(cancellationToken);
                Assert.Equal(receptionId, reception.Id);
                Assert.Equal(now, reception.ArrivedAt);
                Assert.Equal(new ReceptionSignal { Rssi = -72, Snr = 4.25 }, reception.Signal);
                Assert.Equal(PacketAssociation.PublisherAssertedRx, reception.Association);
                Assert.Equal(
                    AdvertSignatureStatus.InvalidSignature,
                    reception.Packet!.SignatureVerification.Status
                );
                Assert.Equal(
                    AdvertSignatureErrorKind.SignatureMismatch,
                    reception.Packet.SignatureVerification.FailureReason
                );
                Assert.Equal(now, reception.Packet.SignatureVerification.CheckedAt);
            }
        }
        Assert.Empty(
            await producer.ReadGroupedHeadAsync<string>(
                "ingestion",
                vt: 0,
                limit: 1,
                cancellationToken
            )
        );
    }

    private static Task<long> SendAsync(
        NpgmqClient client,
        string group,
        string value,
        CancellationToken cancellationToken
    ) =>
        client.SendAsync(
            "ingestion",
            $"\"{value}\"",
            new Dictionary<string, object> { ["x-pgmq-group"] = group },
            cancellationToken
        );

    private static async Task<long> CountAsync(
        NpgsqlDataSource source,
        string table,
        CancellationToken cancellationToken
    )
    {
        // Table names are fixed test constants, never external input.
        await using var command = source.CreateCommand($"SELECT count(*) FROM {table}");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddReception(TrackingDbContext db, Guid id, Instant now)
    {
        byte[] payload = new byte[100];
        byte[] raw = [0x11, 0, .. payload];
        var content = new StoredPacketContent
        {
            Id = Guid.CreateVersion7(),
            Type = PacketType.Advert,
            PayloadVersion = 0,
            Payload = payload,
            PayloadHash = SHA256.HashData(payload),
        };
        var packet = new MeshPacket
        {
            Id = Guid.CreateVersion7(),
            Content = content,
            ContentId = content.Id,
            Type = PacketType.Advert,
            RouteType = RouteType.Flood,
            PathPrefixWidth = 1,
            RawPacket = raw,
            PacketHash = SHA256.HashData(raw),
            PayloadHash = content.PayloadHash,
            ShortHash = "testhash",
            SignatureVerification = new()
            {
                Status = AdvertSignatureStatus.InvalidSignature,
                FailureReason = AdvertSignatureErrorKind.SignatureMismatch,
                CheckedAt = now,
            },
        };
        var observer = new StoredObserver
        {
            Id = Guid.CreateVersion7(),
            PublicKey = new byte[32],
            ModifiedAt = now,
        };
        var broker = new StoredBroker
        {
            Id = Guid.CreateVersion7(),
            Name = "Synthetic",
            Endpoint = "mqtt://synthetic.invalid:1883",
            ModifiedAt = now,
        };
        var region = new StoredRegion
        {
            Id = Guid.CreateVersion7(),
            Code = "TST",
            CountryCode = "LT",
        };
        db.MeshPacketReceptions.Add(
            new MeshPacketReception
            {
                Id = id,
                Observer = observer,
                ObserverId = observer.Id,
                Broker = broker,
                BrokerId = broker.Id,
                Region = region,
                RegionCode = region.Code,
                Packet = packet,
                PacketId = packet.Id,
                ArrivedAt = now,
                Signal = new() { Rssi = -72, Snr = 4.25 },
                Association = PacketAssociation.PublisherAssertedRx,
            }
        );
    }

    private class TestDatabase(string connectionString, string adminConnectionString, string name)
        : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public TrackingDbContext CreateContext() =>
            new(
                TrackingDatabase
                    .Configure(new DbContextOptionsBuilder<TrackingDbContext>(), ConnectionString)
                    .Options
            );

        public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken)
        {
            var port = int.Parse(Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT")!);
            var admin = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Username = "pgmq_test",
                Database = "postgres",
                Pooling = false,
                Timeout = 5,
                CommandTimeout = 15,
            };
            var name = "pgmq_spike_" + Guid.NewGuid().ToString("N");
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
                await using var db = database.CreateContext();
                await db.Database.EnsureCreatedAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE EXTENSION pgmq VERSION '1.13.1'",
                    cancellationToken
                );
                await db.Database.OpenConnectionAsync(cancellationToken);
                var client = new NpgmqClient((NpgsqlConnection)db.Database.GetDbConnection());
                Assert.Equal(
                    new Version(1, 13, 1),
                    await client.GetPgmqVersionAsync(cancellationToken)
                );
                await client.CreateQueueAsync("ingestion", cancellationToken);
                await client.CreateFifoIndexAsync("ingestion", cancellationToken);
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
}
