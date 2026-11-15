using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pulsas.Persistence.Migrations;

[DbContext(typeof(TrackingDbContext))]
[Migration("20261115000100_DurableReportQueues")]
public class DurableReportQueues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE EXTENSION IF NOT EXISTS pgmq;
            DO $$
            BEGIN
                IF (SELECT string_to_array(extversion, '.')::int[] < ARRAY[1,11,1]
                    FROM pg_extension WHERE extname = 'pgmq') THEN
                    RAISE EXCEPTION 'PGMQ 1.11.1 or later is required for durable grouped processing';
                END IF;
            END $$;
            SELECT pgmq.create('ingestion');
            SELECT pgmq.create('packet_content');
            SELECT pgmq.create_fifo_index('packet_content');
            SELECT pgmq.create('ingestion_quarantine');
            """
        );

    // Queue rollback must not silently delete accepted reports or quarantine evidence.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Drain and explicitly retire durable queues before reverting this migration."
        );
}
