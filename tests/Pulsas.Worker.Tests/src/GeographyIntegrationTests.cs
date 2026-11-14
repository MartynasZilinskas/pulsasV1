using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NodaTime;
using Npgsql;
using Pulsas.Persistence;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Nodes;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Receptions;
using Pulsas.Persistence.Regions;
using Xunit;

namespace Pulsas.Worker.Tests;

// Uses the migrated, retained database. Ordinary cases roll back; concurrency
// cases remove only their own UUID-tagged rows. No database or schema is reset.
public class GeographyIntegrationTests
{
    private static TrackingDbContext CreateContext() =>
        new(
            TrackingDatabase
                .Configure(
                    new DbContextOptionsBuilder<TrackingDbContext>(),
                    Environment.GetEnvironmentVariable("PULSAS_GEOGRAPHY_TEST_CONNECTION")!
                )
                .Options
        );

    private static Point Point(double x, double y = 0.5) => new(x, y) { SRID = 4326 };

    private static MultiPolygon Shape(double left, double right) =>
        (MultiPolygon)
            new WKTReader().Read(
                $"SRID=4326;MULTIPOLYGON((({left} 0,{right} 0,{right} 1,{left} 1,{left} 0)))"
            );

    private static StoredRegion Region(string code, double left, double right) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            CountryCode = "LT",
            Shape = Shape(left, right),
        };

    private static MeshNode Node(Point? point) =>
        new()
        {
            Id = Guid.NewGuid(),
            PublicKey = RandomNumberGenerator.GetBytes(32),
            Location = point,
        };

    [GeographyFact]
    public async Task EF_returns_assignments_for_insert_movement_outside_and_cleared_location()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var a = Region("ZQA", 0, 1);
        var b = Region("ZQB", 1, 2);
        db.Regions.AddRange(a, b);
        await db.SaveChangesAsync(ct);
        var node = Node(Point(0.5));
        node.LocationRegionId = Guid.NewGuid(); // EF must ignore caller-supplied assignments.
        db.MeshNodes.Add(node);
        await db.SaveChangesAsync(ct);
        Assert.Equal(a.Id, node.LocationRegionId);
        node.Location = Point(1.5);
        await db.SaveChangesAsync(ct);
        Assert.Equal(b.Id, node.LocationRegionId);
        node.Location = Point(5);
        await db.SaveChangesAsync(ct);
        Assert.Null(node.LocationRegionId);
        node.Location = null;
        await db.SaveChangesAsync(ct);
        Assert.Null(node.LocationRegionId);
        var noLocation = Node(null);
        db.MeshNodes.Add(noLocation);
        await db.SaveChangesAsync(ct);
        Assert.Null(noLocation.LocationRegionId);
    }

    [GeographyFact]
    public async Task Region_insert_shape_change_code_change_and_delete_refresh_existing_nodes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var node = Node(Point(0.5));
        db.MeshNodes.Add(node);
        await db.SaveChangesAsync(ct);
        Assert.Null(node.LocationRegionId);
        var b = Region("ZQB", 0, 1);
        db.Regions.Add(b);
        await db.SaveChangesAsync(ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(b.Id, node.LocationRegionId);
        b.Shape = Shape(2, 3);
        await db.SaveChangesAsync(ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Null(node.LocationRegionId);
        b.Shape = Shape(0, 1);
        await db.SaveChangesAsync(ct);
        var a = Region("ZQA", 1, 2);
        db.Regions.Add(a);
        node.Location = Point(1); // Shared boundary, not overlapping polygon interiors.
        await db.SaveChangesAsync(ct);
        // EF can send the node update before the region insert. The region
        // trigger repairs the database, not an already-tracked node instance.
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(a.Id, node.LocationRegionId);
        // Code is an EF alternate key; exercise the supported SQL/manual edit path.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE regions SET \"Code\" = 'ZQC' WHERE \"Id\" = {a.Id}",
            ct
        );
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(b.Id, node.LocationRegionId);
        db.Regions.Remove(b);
        await db.SaveChangesAsync(ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(a.Id, node.LocationRegionId);
        db.Regions.Remove(a);
        await db.SaveChangesAsync(ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Null(node.LocationRegionId);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
    }

    [GeographyFact]
    public async Task Unchanged_location_and_unrelated_changes_do_not_run_spatial_assignment()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var node = Node(Point(0.5));
        db.MeshNodes.Add(node);
        await db.SaveChangesAsync(ct);
        // Inject a transaction-local tripwire immediately before the production
        // lookup, preserving the actual trigger's early-return logic.
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE definition text;
            BEGIN
                definition := pg_get_functiondef('public.assign_node_location_region()'::regprocedure);
                IF strpos(definition, 'NEW."LocationRegionId" :=') = 0 THEN
                    RAISE EXCEPTION 'Lookup instrumentation point missing';
                END IF;
                EXECUTE replace(definition, 'NEW."LocationRegionId" :=',
                    'RAISE EXCEPTION ''Unexpected assignment''; NEW."LocationRegionId" :=');
            END $$;
            """,
            ct
        );
        node.Name = "unchanged-location-test";
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE node_information SET \"Location\" = \"Location\" WHERE \"Id\" = {node.Id}",
            ct
        );
        // Ensure the guard fails for a real change. Rollback restores the original function.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE node_information SET \"Location\" = ST_SetSRID(ST_MakePoint(4,0),4326) WHERE \"Id\" = {node.Id}",
                ct
            )
        );
        Assert.Equal("Unexpected assignment", error.MessageText);
    }

    [GeographyFact]
    public async Task SQL_cannot_override_derived_assignment_and_bulk_region_deletion_is_reconciled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var a = Region("ZQA", 0, 1);
        var b = Region("ZQB", 1, 2);
        db.Regions.AddRange(a, b);
        var node = Node(Point(0.5));
        db.MeshNodes.Add(node);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE node_information SET \"LocationRegionId\" = {b.Id} WHERE \"Id\" = {node.Id}",
            ct
        );
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(a.Id, node.LocationRegionId);
        await db.Regions.Where(r => r.Id == a.Id || r.Id == b.Id).ExecuteDeleteAsync(ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Null(node.LocationRegionId);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
    }

    [GeographyFact]
    public async Task Migrations_are_repeatable_and_install_the_spatial_index_and_deferred_fk()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True(
            await db
                .Database.SqlQuery<bool>(
                    $"""
                    SELECT condeferrable AND condeferred AS "Value"
                    FROM pg_constraint WHERE conrelid = 'public.node_information'::regclass
                    AND conname = 'FK_node_information_regions_LocationRegionId'
                    """
                )
                .SingleAsync(ct)
        );
        Assert.True(
            await db
                .Database.SqlQuery<bool>(
                    $"""
                    SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname = 'public'
                        AND indexname = 'ix_regions_shape_gist' AND indexdef LIKE '%USING gist%') AS "Value"
                    """
                )
                .SingleAsync(ct)
        );
    }

    [GeographyFact]
    public async Task Historical_reception_fk_still_blocks_region_deletion_without_changing_assignment()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var region = Region("ZQA", 0, 1);
        var node = Node(Point(0.5));
        var observer = new StoredObserver
        {
            Id = Guid.NewGuid(),
            PublicKey = RandomNumberGenerator.GetBytes(32),
        };
        var broker = new StoredBroker
        {
            Id = Guid.NewGuid(),
            Name = "geography-test",
            Endpoint = $"mqtt://{Guid.NewGuid():N}",
        };
        db.AddRange(region, node, observer, broker);
        db.Add(
            new MeshPacketReception
            {
                Id = Guid.NewGuid(),
                RegionCode = region.Code,
                ObserverId = observer.Id,
                BrokerId = broker.Id,
                ArrivedAt = Instant.FromUtc(2026, 11, 14, 0, 0),
            }
        );
        await db.SaveChangesAsync(ct);
        await transaction.CreateSavepointAsync("before_delete", ct);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Regions.Where(r => r.Id == region.Id).ExecuteDeleteAsync(ct)
        );
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        await transaction.RollbackToSavepointAsync("before_delete", ct);
        await db.Entry(node).ReloadAsync(ct);
        Assert.Equal(region.Id, node.LocationRegionId);
        Assert.True(await db.Regions.AnyAsync(r => r.Id == region.Id, ct));
    }

    [GeographyFact]
    public async Task Repeatable_read_writes_report_the_current_qualification_limit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            ct
        );
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE node_information SET \"Name\" = \"Name\" WHERE false",
                ct
            )
        );
        Assert.Equal("0A000", error.SqlState);
    }

    [GeographyTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Concurrent_node_and_region_changes_remain_consistent(
        bool regionFirst,
        bool delete,
        bool insert = false
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        var region = Region("ZQR", 0, 1);
        var node = Node(Point(0.5));
        await using var verify = CreateContext();
        verify.Add(region);
        if (!insert)
        {
            verify.Add(node);
        }
        await verify.SaveChangesAsync(ct);
        try
        {
            await using var first = CreateContext();
            await using var second = CreateContext();
            await using var tx1 = await first.Database.BeginTransactionAsync(ct);
            await using var tx2 = await second.Database.BeginTransactionAsync(ct);
            var firstOperation = regionFirst ? ChangeRegion(first) : ChangeNode(first);
            await firstOperation;
            var secondOperation = regionFirst ? ChangeNode(second) : ChangeRegion(second);
            var pid = ((NpgsqlConnection)second.Database.GetDbConnection()).ProcessID;
            await WaitForAdvisoryWaitAsync(verify, pid, ct);
            Assert.False(secondOperation.IsCompleted);
            await tx1.CommitAsync(ct);
            await secondOperation;
            await tx2.CommitAsync(ct);
            var stored = await verify
                .MeshNodes.AsNoTracking()
                .SingleAsync(n => n.Id == node.Id, ct);
            Assert.Equal(delete ? null : region.Id, stored.LocationRegionId);
            var expected = await verify
                .Regions.Where(r => r.Shape != null && r.Shape.Covers(stored.Location!))
                .OrderBy(r => r.Code)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
            Assert.Equal(expected, stored.LocationRegionId);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await verify.MeshNodes.Where(n => n.Id == node.Id).ExecuteDeleteAsync(cleanup.Token);
            await verify.Regions.Where(r => r.Id == region.Id).ExecuteDeleteAsync(cleanup.Token);
        }

        Task ChangeNode(TrackingDbContext db) =>
            insert
                ? db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO node_information ("Id", "PublicKey", "SourceType", "Location")
                    VALUES ({node.Id}, {node.PublicKey}, 'unknown', ST_SetSRID(ST_MakePoint(2.5,0.5),4326))
                    """,
                    ct
                )
                : db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE node_information SET \"Location\" = ST_SetSRID(ST_MakePoint(2.5,0.5),4326) WHERE \"Id\" = {node.Id}",
                    ct
                );
        Task ChangeRegion(TrackingDbContext db) =>
            delete
                ? db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM regions WHERE \"Id\" = {region.Id}",
                    ct
                )
                : db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE regions SET \"Shape\" = ST_Multi(ST_MakeEnvelope(2,0,3,1,4326)) WHERE \"Id\" = {region.Id}",
                    ct
                );
    }

    private static async Task WaitForAdvisoryWaitAsync(
        TrackingDbContext observer,
        int pid,
        CancellationToken cancellationToken
    )
    {
        while (
            !await observer
                .Database.SqlQuery<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid = {pid} AND locktype = 'advisory' AND NOT granted) AS \"Value\""
                )
                .SingleAsync(cancellationToken)
        )
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    [GeographyFact]
    public async Task Concurrent_region_writers_serialize_before_rebuilding()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        var a = Region("ZQA", 0, 1);
        var b = Region("ZQB", 1, 2);
        var node = Node(Point(0.5));
        await using var verify = CreateContext();
        verify.AddRange(a, b, node);
        await verify.SaveChangesAsync(ct);
        try
        {
            await using var first = CreateContext();
            await using var second = CreateContext();
            await using var tx1 = await first.Database.BeginTransactionAsync(ct);
            await using var tx2 = await second.Database.BeginTransactionAsync(ct);
            await first.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE regions SET \"Shape\" = NULL WHERE \"Id\" = {a.Id}",
                ct
            );
            var change = second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE regions SET \"Shape\" = ST_Multi(ST_MakeEnvelope(0,0,1,1,4326)) WHERE \"Id\" = {b.Id}",
                ct
            );
            await WaitForAdvisoryWaitAsync(
                verify,
                ((NpgsqlConnection)second.Database.GetDbConnection()).ProcessID,
                ct
            );
            Assert.False(change.IsCompleted);
            await tx1.CommitAsync(ct);
            await change;
            await tx2.CommitAsync(ct);
            await verify.Entry(node).ReloadAsync(ct);
            Assert.Equal(b.Id, node.LocationRegionId);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await verify.MeshNodes.Where(n => n.Id == node.Id).ExecuteDeleteAsync(cleanup.Token);
            await verify
                .Regions.Where(r => r.Id == a.Id || r.Id == b.Id)
                .ExecuteDeleteAsync(cleanup.Token);
        }
    }

    [GeographyFact]
    public async Task Unchanged_region_shape_and_name_edits_do_not_rebuild_assignments()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var region = Region("ZQA", 0, 1);
        db.Add(region);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE PROCEDURE public.rebuild_node_location_regions()
            LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Unexpected rebuild'; END $$;
            """,
            ct
        );
        region.Name = "name-only edit";
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE regions SET \"Shape\" = \"Shape\" WHERE \"Id\" = {region.Id}",
            ct
        );
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE regions SET \"Shape\" = NULL WHERE \"Id\" = {region.Id}",
                ct
            )
        );
        Assert.Equal("Unexpected rebuild", error.MessageText);
    }
}
