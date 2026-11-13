# Pulsas.Persistence

EF Core/PostgreSQL models migrated from Pulsas, with NodaTime and NetTopologySuite.
`TrackingDatabase.Configure` registers native application enums and both provider
plugins; MeshCore byte-backed enums retain numeric `smallint` storage.

Includes signature assessment, configured chat identities and routing-scope
matches. Keys are not stored. `Active`/`Public` are persistence policy fields;
future public read paths must filter them before exposing data.

No migrations, database initialization, ingestion writes or configuration
reconciliation are included yet. Provider-model tests generate SQL without
connecting to PostgreSQL; they do not establish live database readiness.
