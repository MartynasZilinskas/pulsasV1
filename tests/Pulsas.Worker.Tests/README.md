# Dummy Worker tests

## Broker-free path assessment

`PathAssessmentIntegrationTests` submits synthetic reports to the real durable
receiver, processes both queues, and inspects persisted reception assessments.
It exercises the Core assessment manager using `TrackingDbContext` directly,
including the shared delivery transaction and single-command knowledge read.
It uses the disposable loopback PostgreSQL container below, creates UUID-named
`path_test_*` databases, applies production migrations, and drops only those databases.
Neither MQTT nor a running Worker executable is required. One case starts Gateway
on an ephemeral loopback HTTP port and queries the same stored packet through two
different receptions before and after evidence changes. It also runs the unchanged
reception-browser operations copied from the old client into
`tests/fixtures/graphql/reception-browser.graphql`. Do not set a retained database
connection for this suite.

The `Mesh_traffic_` Gateway tests cover the summary, trend, and distributions.
The summary fixture checks that two observers hearing the same frame in different
hours contribute to both trend buckets but count as one wire packet in the window
summary. A different path adds a wire packet without adding a shared payload.

For timing output, run `Mesh_traffic_reads_stay_within_gateway_limits_on_a_synthetic_month`
against the disposable database with `--logger 'console;verbosity=detailed'`.
It ingests the canonical packet and observer identities through durable delivery,
then seeds bulk receptions via EF for read qualification: 2,000 receptions/day
for 30 days, eight observers, one region, 128 wire packets over 32 payloads,
75% of arrivals in a roughly 30-minute daily burst, and independent missing
SNR (10%) and RSSI (20%) samples. It measures each full GraphQL read at 24h, 7d,
and 30d after warm-up, with and without an observer filter, three times. On a
local PostgreSQL 18 run, the slowest observed unfiltered HTTP reads were:

| Read | SQL reads | 24h (2,000 rows) | 7d (14,000 rows) | 30d (60,000 rows) |
| --- | ---: | ---: | ---: | ---: |
| Summary | 3 | 9.2 ms | 14.6 ms | 53.0 ms |
| Trend | 1 | 19.6 ms | 80.8 ms | 369.0 ms |
| Distributions | 2 | 11.2 ms | 6.7 ms | 25.6 ms |

The slowest SQL read was the unfiltered 30-day trend (286.3 ms).
All measured requests fit the 2-second command and 3-second Gateway timeout
settings. This workload does not justify rollups. Before adding them, profile
the 30-day trend with higher packet cardinality and concurrent ingestion and reads,
and the summary with a denser tag catalogue. If those reads approach the limits,
inspect the query plans first. Any rollup must preserve exact window totals:
adding per-bucket distinct counts would double-count packets and payloads seen
in several buckets. These timings do not qualify production load or ingestion throughput.

`Shared_payloads_expose_canonical_packets_and_earliest_retained_representative`
ingests repeat receptions and distinct wire packets through production delivery.
It checks payload ordering, totals, representative selection (including backfill,
ties, and removal), canonical packet and reception nodes, and scoped history
cursors through Gateway HTTP.

```sh
PULSAS_PGMQ_TEST_PORT=<disposable-database-port> dotnet test tests/Pulsas.Worker.Tests --no-restore \
  --filter 'FullyQualifiedName~PathAssessmentIntegrationTests|FullyQualifiedName~PathAssessmentMigrationTests|FullyQualifiedName~NetworkPacketGeneratorTests|FullyQualifiedName~ReceptionGraphQLTests'
```

The network inventories and generator are adapted from the previous repository's
fixture-driven harness. Their signing seeds are synthetic test identities. Directed
links, hearing-before-forwarding, deterministic virtual-time ordering, duplicate
hearings and per-execution forwarding suppression are test ground truth, never
preloaded resolver evidence. Only generated packet reports enter production ingestion.
The adapter translates the fixture broker to a synthetic WebSocket endpoint without
connecting; simulated hearing time is a report claim, separate from local arrival.
Normal persistence still leaves `HeardAt` null. This qualifies analyzer behavior,
not physical RF or firmware timing.

`PathAssessmentMigrationTests` checks an upgrade containing pre-existing synthetic
reception evidence by comparing canonical rows before/after migration and repeat
application. It creates and removes only its own `path_migration_*` database.

`ReceptionGraphQLTests` checks the local schema, ID behavior, paging, and query
cost limits with the production GraphQL executor and no database or network.

Normal solution tests validate configuration and callback acknowledgment after
durable acceptance. The opt-in broker test exercises the production consumer,
both subscriptions, forwarding to a test-only receiver, and shutdown.
It publishes only synthetic, non-retained data in region `TST`.

Against an isolated local WebSocket broker:

```sh
PULSAS_WORKER_MQTT_ENDPOINT=ws://127.0.0.1:9001/mqtt \
  mise exec -- dotnet test tests/Pulsas.Worker.Tests --no-restore
```

Do not point this at a shared or external broker. This proves transport receipt
and durable-acceptance forwarding using a test-only receiver, not database persistence.
For the real generator path, run Aspire, wait for Worker health, start traffic,
and inspect `packet_receptions`, queue archives and `node_information` in Drizzle.

## Cached PostGIS geography

Run the opt-in suite through Aspire so it receives the existing database connection
without extracting passwords. From the repository root, start Aspire with:

```sh
./aspire.sh run -- --GeographyTests=true
```

The `geography-tests` resource waits for migrations, runs against **the same
retained `pulsas_dev` database**, and exits. Inspect it in the dashboard or use
`./aspire.sh logs geography-tests`; rerun with
`./aspire.sh resource geography-tests restart`. Normal startup omits this resource.
With a separately authorized local connection, the equivalent test filter is:

```sh
mise exec -- dotnet test tests/Pulsas.Worker.Tests \
  --filter 'FullyQualifiedName~GeographyIntegrationTests|FullyQualifiedName~AdvertIntegrationTests'
```

Set `PULSAS_GEOGRAPHY_TEST_CONNECTION` securely for that invocation; tests skip if
it is missing. Never point the suite at a remote/shared database. Tests use reserved
synthetic region codes `ZQA`, `ZQB`, `ZQC`, `ZQR` around coordinates 0–5 degrees.
Most roll back; concurrency cases commit UUID-tagged rows and remove only those
rows afterward. Two rollback-only cases temporarily instrument a database function
or procedure to prove unchanged inputs skip recomputation. They restore definitions
on transaction rollback. No databases, schemas, volumes, or existing rows are reset.

Coverage includes EF hydration, interior/outside/null positions, shared boundaries,
shape/code edits, direct/bulk deletion, protected historical references, assignment
tampering, unchanged-input caching, repeatable migration application, and concurrent
node/region writes and deletions in both lock orders. Lock assertions observe real
PostgreSQL advisory-lock waits, not timing-only sleeps. Repeatable-read writes are
explicitly rejected by the current implementation.

The same Aspire resource runs `AdvertIntegrationTests`, exercising the real manager,
codec, signature verification, atomic node writer and PostGIS assignment. The fixture
requires the retained KUN/PLQ/SQQ/VNO regions and never imports or replaces them.
It verifies ordering, concurrent public-key upserts, omissions/reset, malformed metadata,
coordinate bounds, signature rejection and future-skew policy. Each case removes only
its own random-key node. Generated demonstration nodes are not removed by this suite.

`AdvertIntegrationTestsDelivery` exercises the production queue receiver and processor
against test-owned random PGMQ queues in the migrated database. It verifies durable
acceptance, transactional ingestion/content rollback, retry exhaustion/quarantine,
per-reception clock context and malformed-packet evidence. It drops only its queues
and removes its own generated entities. It does not drain or purge production queues.

The GroupText cases in the same fixture use synthetic in-memory channel
configuration, test-owned channel rows and the real encryption codec. They exercise
configured matching, canonical message deduplication across attempts/paths, packet
and reception links, transaction rollback and retries. The reception race test seeds
the observer, broker and packet first, synchronizes two inserts, then asserts that
the exact `ux_receptions_identity` conflict is retried without duplicate content work.
Tests never read operator key files.

## Real Worker executable qualification

`WorkerPipelineIntegrationTests` requires both `PULSAS_PGMQ_TEST_PORT` and
`PULSAS_WORKER_MQTT_ENDPOINT`. Use only the separate disposable, loopback database
container described below and an isolated Mosquitto WebSocket broker. It creates
and migrates a UUID-named database, launches the built Worker executable with a
clean environment, and enables one synthetic hashtag channel. No operator secret
files, user-secrets settings, Aspire services or telemetry exporters are inherited.
The database user is the synthetic `pgmq_test` role; no credentials are extracted.

The real MQTT callback, durable receiver, ingestion queue and four-task content
pool process ADVERT and encrypted GroupText reports. The cases verify Unicode and
whitespace-only region IDs, ignored missing/malformed/mismatched `origin_id`,
100 ns timestamp distinctions, duplicate logs, canonical chat links, queue
archives and empty pending/quarantine queues. Worker logs must not contain raw
packet bytes or chat text. The test terminates its own Worker, drops only its
database and removes its ignored `.tmp/` scratch directory. This teardown does
not qualify graceful restart recovery or production deployment.

For example, with the disposable services' actual published ports:

```sh
PULSAS_PGMQ_TEST_PORT=<database-port> \
PULSAS_WORKER_MQTT_ENDPOINT=ws://127.0.0.1:<mqtt-port>/mqtt \
  dotnet test tests/Pulsas.Worker.Tests --no-restore \
  --filter 'FullyQualifiedName~WorkerPipelineIntegrationTests'
```

Migration safety is also checked against pre-migration synthetic evidence: apply
through `DurableReportQueues`, insert region/observer/broker/reception rows, run
the production `DatabaseSetup` executable, and compare canonical JSON checksums
of all pre-existing fields. Legacy identity/timestamp fields must remain null;
running `DatabaseSetup` again must be a no-op. This check must not target a
retained database. Synthetic geography rectangles can qualify the existing
KUN/PLQ/SQQ/VNO test expectations; they are not administrative border evidence.

## PGMQ transaction and ordering spike

Npgmq **1.13.0** is used by Worker and this boundary spike. These opt-in tests use the
production EF model but do not wire MQTT ingestion, path assessment, or frontend
events. They create randomly named databases and drop **only those databases**
afterward. Never point them at an existing development or provider database.

From the repository root, provision a separate disposable local container:

```sh
podman build -t localhost/pulsas-pgmq-spike:pg18-pgmq1.13.1 \
  -f projects/AppHost/containers/postgres/Containerfile projects/AppHost/containers/postgres
podman run -d --name pulsas-pgmq-spike --pull=never \
  -e POSTGRES_USER=pgmq_test -e POSTGRES_HOST_AUTH_METHOD=trust \
  -p 127.0.0.1::5432 \
  --health-cmd='pg_isready -h 127.0.0.1 -U pgmq_test -d postgres' \
  --health-interval=1s --health-retries=30 \
  localhost/pulsas-pgmq-spike:pg18-pgmq1.13.1
podman wait --condition=healthy pulsas-pgmq-spike
podman port pulsas-pgmq-spike 5432
```

Use the printed port for `PULSAS_PGMQ_TEST_PORT` (tests always connect to
`127.0.0.1` as the synthetic `pgmq_test` role, without a password):

```sh
PULSAS_PGMQ_TEST_PORT=<printed-port> mise exec -- dotnet test tests/Pulsas.Worker.Tests \
  --filter 'FullyQualifiedName~PgmqIntegrationTests'
podman rm -f -v pulsas-pgmq-spike
```

The trust-authenticated container is loopback-only, disposable, and contains only
synthetic data; never reuse these authentication settings for normal development
or deployment. Remove the container and its anonymous volume even if a test fails.

The tests enable PGMQ **1.13.1** in their new databases and verify:

- Same-group FIFO and cross-group progress on separate connections, including
  an immediately expired visibility timeout and archive-before-commit.
- Queue completion and actual packet/content/reception writes share EF's
  connection/transaction and commit or roll back atomically.
- Native enums, NodaTime timestamps, signal and signature field groups survive
  a PostgreSQL round trip.

No throughput, restart recovery of a real consumer, reception deduplication,
frontend outbox, or provider deployment is established by this spike.
