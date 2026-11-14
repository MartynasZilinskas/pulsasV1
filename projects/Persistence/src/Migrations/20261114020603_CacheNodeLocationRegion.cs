using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulsas.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CacheNodeLocationRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deferral lets the region AFTER DELETE trigger repair assignments before
            // FK validation. EF models NO ACTION; deferrability is migration-managed.
            migrationBuilder.Sql(
                """
                ALTER TABLE public.node_information
                    DROP CONSTRAINT "FK_node_information_regions_LocationRegionId";
                ALTER TABLE public.node_information
                    ADD CONSTRAINT "FK_node_information_regions_LocationRegionId"
                    FOREIGN KEY ("LocationRegionId") REFERENCES public.regions ("Id")
                    ON DELETE NO ACTION DEFERRABLE INITIALLY DEFERRED;
                """
            );
            migrationBuilder
                .CreateIndex(name: "ix_regions_shape_gist", table: "regions", column: "Shape")
                .Annotation("Npgsql:IndexMethod", "gist");
            migrationBuilder.Sql(
                """
                -- Reserved advisory lock (1347767379, 1): Pulsas geography assignments.
                -- Node writers share it; region writers exclude them until commit.
                CREATE FUNCTION public.lock_node_geography() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF current_setting('transaction_isolation') <> 'read committed' THEN
                        RAISE EXCEPTION 'Geography writes currently require READ COMMITTED isolation'
                            USING ERRCODE = '0A000';
                    END IF;
                    PERFORM pg_advisory_xact_lock_shared(1347767379, 1);
                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION public.lock_region_geography() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF current_setting('transaction_isolation') <> 'read committed' THEN
                        RAISE EXCEPTION 'Geography writes currently require READ COMMITTED isolation'
                            USING ERRCODE = '0A000';
                    END IF;
                    PERFORM pg_advisory_xact_lock(1347767379, 1);
                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION public.assign_node_location_region() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."Location" IS NOT DISTINCT FROM OLD."Location"
                            AND NEW."LocationRegionId" IS NOT DISTINCT FROM OLD."LocationRegionId" THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    NEW."LocationRegionId" := (
                        SELECT r."Id" FROM public.regions r
                        WHERE ST_Covers(r."Shape", NEW."Location")
                        ORDER BY r."Code" COLLATE "C"
                        LIMIT 1
                    );
                    RETURN NEW;
                END;
                $$;

                -- Call after obtaining the EXCLUSIVE advisory lock, in a separate
                -- command, so the lookup takes a fresh READ COMMITTED snapshot.
                CREATE PROCEDURE public.rebuild_node_location_regions()
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('transaction_isolation') <> 'read committed'
                        OR NOT EXISTS (
                            SELECT 1 FROM pg_locks
                            WHERE locktype = 'advisory' AND pid = pg_backend_pid()
                                AND classid = 1347767379 AND objid = 1 AND objsubid = 2
                                AND mode = 'ExclusiveLock' AND granted
                        ) THEN
                        RAISE EXCEPTION 'Acquire the geography exclusive transaction lock at READ COMMITTED before rebuilding'
                            USING ERRCODE = '55000';
                    END IF;
                    WITH assignments AS MATERIALIZED (
                        SELECT n."Id", (
                            SELECT r."Id" FROM public.regions r
                            WHERE ST_Covers(r."Shape", n."Location")
                            ORDER BY r."Code" COLLATE "C"
                            LIMIT 1
                        ) AS region_id
                        FROM public.node_information n
                    )
                    UPDATE public.node_information n
                    SET "LocationRegionId" = a.region_id
                    FROM assignments a
                    WHERE n."Id" = a."Id"
                        AND n."LocationRegionId" IS DISTINCT FROM a.region_id;
                END;
                $$;

                CREATE FUNCTION public.refresh_regions_after_insert() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM new_regions) THEN
                        CALL public.rebuild_node_location_regions();
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION public.refresh_regions_after_delete() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM old_regions) THEN
                        CALL public.rebuild_node_location_regions();
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION public.refresh_regions_after_update() RETURNS trigger
                LANGUAGE plpgsql VOLATILE AS $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM old_regions o FULL JOIN new_regions n USING ("Id")
                        WHERE o."Shape" IS DISTINCT FROM n."Shape"
                            OR o."Code" IS DISTINCT FROM n."Code"
                    ) THEN
                        CALL public.rebuild_node_location_regions();
                    END IF;
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER lock_node_geography
                    BEFORE INSERT OR UPDATE OR DELETE ON public.node_information
                    FOR EACH STATEMENT EXECUTE FUNCTION public.lock_node_geography();
                CREATE TRIGGER assign_node_location_region
                    BEFORE INSERT OR UPDATE ON public.node_information
                    FOR EACH ROW EXECUTE FUNCTION public.assign_node_location_region();
                CREATE TRIGGER lock_region_geography
                    BEFORE INSERT OR UPDATE OR DELETE ON public.regions
                    FOR EACH STATEMENT EXECUTE FUNCTION public.lock_region_geography();
                CREATE TRIGGER refresh_regions_after_insert
                    AFTER INSERT ON public.regions REFERENCING NEW TABLE AS new_regions
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refresh_regions_after_insert();
                CREATE TRIGGER refresh_regions_after_delete
                    AFTER DELETE ON public.regions REFERENCING OLD TABLE AS old_regions
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refresh_regions_after_delete();
                CREATE TRIGGER refresh_regions_after_update
                    AFTER UPDATE ON public.regions
                    REFERENCING OLD TABLE AS old_regions NEW TABLE AS new_regions
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refresh_regions_after_update();

                DO $lock$ BEGIN
                    PERFORM pg_advisory_xact_lock(1347767379, 1);
                END $lock$;
                CALL public.rebuild_node_location_regions();
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER refresh_regions_after_update ON public.regions;
                DROP TRIGGER refresh_regions_after_delete ON public.regions;
                DROP TRIGGER refresh_regions_after_insert ON public.regions;
                DROP TRIGGER lock_region_geography ON public.regions;
                DROP TRIGGER assign_node_location_region ON public.node_information;
                DROP TRIGGER lock_node_geography ON public.node_information;
                DROP FUNCTION public.refresh_regions_after_update();
                DROP FUNCTION public.refresh_regions_after_delete();
                DROP FUNCTION public.refresh_regions_after_insert();
                DROP PROCEDURE public.rebuild_node_location_regions();
                DROP FUNCTION public.assign_node_location_region();
                DROP FUNCTION public.lock_region_geography();
                DROP FUNCTION public.lock_node_geography();
                ALTER TABLE public.node_information
                    DROP CONSTRAINT "FK_node_information_regions_LocationRegionId";
                ALTER TABLE public.node_information
                    ADD CONSTRAINT "FK_node_information_regions_LocationRegionId"
                    FOREIGN KEY ("LocationRegionId") REFERENCES public.regions ("Id")
                    ON DELETE RESTRICT;
                """
            );
            migrationBuilder.DropIndex(name: "ix_regions_shape_gist", table: "regions");
        }
    }
}
