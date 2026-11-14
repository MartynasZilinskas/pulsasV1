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
and in-memory ADVERT interpretation, not database persistence. For the generator path, run
Aspire, wait for Worker health, start traffic, and inspect Worker's interpretation logs.

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
