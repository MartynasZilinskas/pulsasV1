# Pulsas.Persistence

EF Core/PostgreSQL models migrated from Pulsas, with NodaTime and NetTopologySuite.
`TrackingDatabase.Configure` registers native application enums and both provider
plugins; MeshCore byte-backed enums retain numeric `smallint` storage.

Includes signature assessment, configured chat identities and routing-scope
matches. Keys are not stored. `Active`/`Public` are persistence policy fields;
future public read paths must filter them before exposing data.

## Local database

`./aspire.sh` starts the one retained `pulsas_dev` database on the
`pulsas-v1-postgres-data` volume. The `database-setup` resource applies EF migrations
and exits; Drizzle Studio waits for it and is available at http://localhost:4983.
Restarting does not reset data. Preserve Aspire's existing password store with the
volume. Do not use `EnsureCreated` for this database or delete the volume to update
the schema.

EF Core/Design/Relational and the repository-local `dotnet-ef` tool use **10.0.15**,
the latest stable EF 10 release compatible with the current stable Npgsql provider.
From the repository root:

```sh
mise exec -- dotnet tool restore
mise exec -- dotnet ef migrations add <Name> --project projects/Persistence \
  --output-dir src/Migrations --namespace Pulsas.Persistence.Migrations
mise exec -- dotnet ef migrations has-pending-model-changes --project projects/Persistence
```

The design-time factory uses a nonconnecting placeholder. Real migration execution
uses `ConnectionStrings:pulsas`, injected by Aspire into `DatabaseSetup`; it must
not be copied into source or logs. Inspect generated migrations before applying.
`DatabaseSetup` is a schema tool, not an ingestion service. Development Worker
projects verified ADVERTs to nodes; Gateway does not yet write application data.

## ADVERT node projection

`IAdvertNodeManager.ProcessAdvertAsync` verifies the actual signed payload at its boundary.
Invalid signatures never create or update nodes. Verified adverts create missing
identities by public key; complete valid metadata is applied atomically with an
EF conditional update timestamp predicate. Missing nodes use ordinary EF insertion;
a competing public-key insertion returns `ProcessAdvertError.ConcurrentNodeCreation`
without an internal retry. The contract is `Either<ProcessAdvertError, Unit>`:
successful writes and no-ops return Right; invalid signatures return Left.
The delivery layer owns retry/dismissal policy and must preserve original receipt
time on retries. Current MQTT handling suppresses acknowledgment on a conflict
but provides no guaranteed redelivery; durable queue ingestion remains pending.
`AdvertMetadataTimestamp` is nullable
signed-body Unix seconds, not a reception/hearing time. Only strictly newer claims
update metadata; equal timestamps keep the first accepted values.

- Omitted name/location preserves stored values; an explicitly empty name is stored.
- Explicit `(0,0)` clears location and its derived region, matching Beacon. Either
  coordinate alone may be zero. Latitude/longitude bounds are inclusive ±90°/±180°.
  `Pulsas.Core` owns coordinate validity through its immutable `GeoLocation` primitive;
  ADVERT reset policy and conversion to NTS points remain in the persistence writer.
- Empty, malformed, unsupported, out-of-range, or NUL-containing metadata does not
  overwrite any metadata or advance its watermark. Verified identity creation is
  still allowed. The codec remains lossless; PostgreSQL cannot store NUL text.
- Metadata more than five minutes ahead of trusted Worker receipt time is ignored;
  the publisher's report timestamp is not used as the clock reference.
- Existing IDs, creation times and source classifications survive updates. New
  identities use `ObservedAdvert`. Packet references and radio-hearing timestamps
  are not invented: packet/content/reception persistence remains unimplemented.

Worker awaits database completion independently of logging. Unexpected failures
propagate and disable automatic acknowledgment; signature rejections are logged
and acknowledged as terminal dismissals. This development
consumer still uses clean MQTT sessions, so this is not a durable retry/queue design.

## Cached node geography

PostGIS triggers maintain `MeshNode.LocationRegionId` for **all database writers**,
including direct SQL/Drizzle edits:

- Node insertion or an actual location change runs `ST_Covers` against stored
  `regions.Shape`, supported by `ix_regions_shape_gist`.
- A missing location or no match stores null. This is a completed result, not a
  request to retry. Unchanged location and unrelated node edits skip the lookup.
- Region insertion/deletion and actual Shape/Code changes rebuild assignments.
  Unchanged shapes and name-only edits do not rebuild. Only changed assignments
  are updated. Regions are expected not to overlap; shared edges are included,
  with the lexicographically first Code (`COLLATE "C"`) winning a boundary tie.
- LocationRegionId is database-generated; callers must not set it. EF reads it
  back on node insert/update. Region changes can update other nodes, so **reload
  previously tracked nodes** after such operations.

Writes are currently qualified for **READ COMMITTED**, enforced at the trigger
boundary. Shared transaction advisory locks allow concurrent node writers;
region writers take the exclusive lock and retain it through commit. Keep write
transactions short. Transactions that will modify both regions and nodes should
take the exclusive geography lock **before either operation** to avoid lock
upgrades. Deadlock victims must retry the whole transaction, not an individual
statement.

The geography lock is reserved as `(1347767379, 1)`. To explicitly reconcile:

```sql
BEGIN;
SELECT pg_advisory_xact_lock(1347767379, 1);
CALL public.rebuild_node_location_regions();
COMMIT;
```

The node region FK is `NO ACTION DEFERRABLE INITIALLY DEFERRED` so deleting a region
can reassign nodes before integrity checks at commit. Deferrability is custom
migration metadata, not an EF snapshot setting; preserve it in future FK changes.
Do not force that constraint to immediate checking during region deletion. Other
region references remain restrictive: historical reception/hearing records can
still prevent deletion without changing any assignments.

Shapes are valid SRID 4326 MultiPolygons; GeoJSON coordinates are
`[longitude, latitude]`. For a GeoJSON **geometry object** (not a FeatureCollection),
an import can use `ST_Multi(ST_GeomFromGeoJSON(...))`. Configuration border files
are **not automatically imported**. Overlap validation/import tooling is not part
of this change; keep the non-overlap guarantee at the region-edit/import boundary.

`examples/geography-demo.sql` supplies clearly labeled synthetic regions/nodes for
manual inspection. It is opt-in, idempotent, and is never seeded by normal startup.
See the Worker test README for real PostGIS regression checks against this same
retained database.
