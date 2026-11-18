using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pulsas.Persistence;
using Xunit;

namespace Pulsas.Worker.Tests;

public class PathAssessmentMigrationTests
{
    [PgmqFact]
    public async Task Additive_assessment_migration_preserves_existing_rows_and_reapplication_is_noop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var admin = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT")!),
            Username = "pgmq_test",
            Database = "postgres",
            Pooling = false,
        };
        var name = "path_migration_" + Guid.NewGuid().ToString("N");
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync(token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", control))
        {
            await create.ExecuteNonQueryAsync(token);
        }
        admin.Database = name;
        try
        {
            await using var db = new TrackingDbContext(
                TrackingDatabase
                    .Configure(
                        new DbContextOptionsBuilder<TrackingDbContext>(),
                        admin.ConnectionString
                    )
                    .Options
            );
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(
                "20261116222318_ReceptionIdentityAndRegionIdentifiers",
                token
            );
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO regions ("Id", "Code", "CountryCode") VALUES ('00000000-0000-0000-0000-000000000001','TST','LT');
                INSERT INTO observers ("Id", "PublicKey", "ModifiedAt") VALUES ('00000000-0000-0000-0000-000000000002',decode(repeat('01',32),'hex'),'2026-11-01Z');
                INSERT INTO brokers ("Id", "Name", "Endpoint", "ModifiedAt") VALUES ('00000000-0000-0000-0000-000000000003','Synthetic','ws://synthetic.invalid/mqtt','2026-11-01Z');
                INSERT INTO packet_contents ("Id","Type","PayloadVersion","PayloadHash","Payload")
                VALUES ('00000000-0000-0000-0000-000000000005',5,0,decode(repeat('02',32),'hex'),decode('abcd','hex'));
                INSERT INTO path_steps ("Id","ContentId","Position","Width","Prefix","CandidatesInitialized")
                VALUES ('00000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000005',0,1,decode('00','hex'),false);
                INSERT INTO packets ("Id","ContentId","LastPathStepId","PacketHash","ShortHash","PayloadHash","Type","RouteType",
                    "PathPrefixWidth","RawPacket","ScopeResolutionStatus","SignatureStatus")
                VALUES ('00000000-0000-0000-0000-000000000007','00000000-0000-0000-0000-000000000005',
                    '00000000-0000-0000-0000-000000000006',decode(repeat('03',32),'hex'),'synthetic',decode(repeat('02',32),'hex'),5,1,1,
                    decode('150100abcd','hex'),'not_assessed','not_checked');
                INSERT INTO path_segments ("Id","PacketId","Position","PathStepId")
                VALUES ('00000000-0000-0000-0000-000000000008','00000000-0000-0000-0000-000000000007',0,'00000000-0000-0000-0000-000000000006');
                INSERT INTO packet_receptions ("Id","ObserverId","BrokerId","RegionCode","ArrivedAt","Association","RawPacket")
                VALUES ('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',
                    '00000000-0000-0000-0000-000000000003','TST','2026-11-01Z','publisher_asserted_rx',decode('010001','hex'));
                UPDATE packet_receptions SET "PacketId" = '00000000-0000-0000-0000-000000000007';
                """,
                token
            );
            var before = await RowsAsync(db, token);
            await db.Database.MigrateAsync(token);
            Assert.Equal(before, await RowsAsync(db, token));
            Assert.Empty(await db.ReceptionPathAssessments.ToArrayAsync(token));
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.MigrateAsync(token);
            Assert.Equal(before, await RowsAsync(db, token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", control);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static Task<string> RowsAsync(TrackingDbContext db, CancellationToken token) =>
        db
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    'receptions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM packet_receptions r),
                    'observers',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM observers r),
                    'brokers',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM brokers r),
                    'regions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM regions r),
                    'contents',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM packet_contents r),
                    'packets',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM packets r),
                    'hops',(SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM path_segments r),
                    'steps',(SELECT jsonb_agg(to_jsonb(r) - 'CandidateCatalogueFingerprint' - 'CandidateNodeIds' ORDER BY r."Id") FROM path_steps r))::text AS "Value"
                """
            )
            .SingleAsync(token);
}
