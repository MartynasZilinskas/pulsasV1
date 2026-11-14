# Dummy Worker tests

Normal solution tests validate configuration and exercise generator reports through
the real packet handler, including recovery after invalid input. The opt-in broker test exercises
the production consumer, both subscriptions, ADVERT interpretation, and shutdown.
It publishes only synthetic, non-retained data in region `TST`.

Against an isolated local WebSocket broker:

```sh
PULSAS_WORKER_MQTT_ENDPOINT=ws://127.0.0.1:9001/mqtt \
  mise exec -- dotnet test tests/Pulsas.Worker.Tests --no-restore
```

Do not point this at a shared or external broker. This proves transport receipt
and ADVERT interpretation using a test-only projection recorder, not database persistence.
For the real generator path, run Aspire, wait for Worker health, start traffic,
and inspect node projection logs and `node_information` in Drizzle.

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

The same Aspire resource runs `AdvertIntegrationTests`, exercising the real handler,
codec, signature verification, atomic node writer and PostGIS assignment. The fixture
requires the retained KUN/PLQ/SQQ/VNO regions and never imports or replaces them.
It verifies ordering, concurrent public-key upserts, omissions/reset, malformed metadata,
coordinate bounds, signature rejection and future-skew policy. Each case removes only
its own random-key node. Generated demonstration nodes are not removed by this suite.

## PGMQ transaction and ordering spike

Npgmq **1.13.0** is currently a test-only dependency. These opt-in tests use the
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
  --health-cmd='pg_isready -U pgmq_test -d postgres' \
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
