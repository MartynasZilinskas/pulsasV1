# PULSAS V1

Incremental, reviewed migration from the existing `pulsas` application. The initial
foundation includes Aspire orchestration, PostgreSQL/PostGIS, Drizzle Studio,
a local MQTT broker, and runnable Worker and GraphQL Gateway hosts. No application
schema, domain GraphQL API, Worker MQTT consumer, background jobs, Web, replay,
or data import is included yet.

## Prerequisites

- .NET SDK **11.0.100**, pinned in `global.json` and `mise.toml`.
- Podman, running and available to the current user (the launch-profile default).
- mise, with the pinned .NET toolchain already installed.
- An ASP.NET Core HTTPS development certificate trusted by your browser. Check
  with `dotnet dev-certs https --check`; follow your platform's certificate setup
  if it is missing or untrusted.

Aspire SDK and packages are pinned to **13.6.5**, matching the reference project.
The AppHost uses the matching CLI through .NET's `dnx`, rather than whichever
global Aspire CLI happens to be installed. The first build/run may restore that
pinned CLI package.
Drizzle Gateway is pinned to **1.7.0**; PostgreSQL/PostGIS uses **18-3.6**.

## Run locally

From the repository root:

```sh
./aspire.sh
```

This defaults to `run`, restores/builds as needed, and uses mise with the pinned
Aspire CLI **13.6.5** through `dotnet dnx`. It avoids the globally installed CLI
and its potentially different SDK selection. Additional CLI commands and arguments
are forwarded unchanged:

```sh
./aspire.sh run
./aspire.sh --version
```

No password or container-runtime exports are required. Aspire generates the
PostgreSQL password on first use and retains it in the AppHost's local .NET user
secrets store (`UserSecretsId: pulsas-v1-apphost`). On this Linux setup, the store
is `~/.microsoft/usersecrets/pulsas-v1-apphost/secrets.json`; it is outside Git and
is not encrypted. Keep it private and retain it alongside the database volume.
The existing development database's password was migrated to this store; startup
does not depend on `.tmp/postgres-password`.

The launch profile selects Podman automatically. With a correctly configured
global Aspire CLI/toolchain, `aspire run` also discovers the AppHost through
`aspire.config.json`; `./aspire.sh` is the reproducible local entry point.
The pinned CLI may be fetched on first use, and container images are pulled if
not already cached. The wrapper does not install mise or the .NET SDK.

The dashboard is at **https://localhost:17241**. Use the private login link printed
by Aspire; anonymous dashboard access is not enabled. Drizzle Studio is at
**http://localhost:4983** and is also linked from the dashboard. Aspire waits for
PostgreSQL/database readiness before starting Drizzle and checks its `/health`
endpoint. Database health does not imply an application schema exists.

Use Ctrl+C to stop the AppHost and its containers. The named volumes remain:

| Resource | Volume | Contents |
| --- | --- | --- |
| PostgreSQL | `pulsas-v1-postgres-data` | Development database `pulsas_dev` |
| Drizzle Studio | `pulsas-v1-drizzle-data` | Studio settings and state |

These volumes are separate from the reference project's
`pulsas-synthetic-postgres-v1`. Keep the user-secrets store across restarts;
generating a different password does not update an initialized database. Do not
delete volumes as a restart or authentication workaround. Keep credentials,
dashboard login tokens, and connection strings out of Git and shared logs.

## Service foundations

| Service | Local endpoint | Current behavior |
| --- | --- | --- |
| Gateway | `http://localhost:5100/graphql` | Operational `serviceInfo { name role }` query only |
| Gateway IDE | `http://localhost:5100/graphql/ui/` | Embedded, development-only GraphQL browser |
| Worker | `http://localhost:5200` | Operational host for future MQTT consumers and background tasks; no tasks registered yet |

Both hosts use `libraries/Pulsas.ServiceDefaults` for OpenTelemetry logs,
ASP.NET Core/HTTP request traces, runtime/request metrics, service discovery,
HTTP client resilience, and development-only `/alive` and `/health` endpoints.
Their health checks currently report **process health only**, not MQTT, database,
or domain readiness. Health probes are excluded from request traces. Browse
service logs, traces, and metrics in the Aspire dashboard.

The hosts do not consume persistence yet, so they receive no database credentials
and do not wait for PostgreSQL. Neither initializes schema or starts ingestion.
The local unauthenticated endpoints are loopback-only; this is not a public
deployment or authentication configuration.

For example, from a separate terminal while Aspire is running:

```sh
curl -H 'Content-Type: application/json' \
  --data '{"query":"{ serviceInfo { name role } }"}' \
  http://localhost:5100/graphql
```

## Local MQTT broker

`./aspire.sh` also starts **Eclipse Mosquitto 2.0.22**, available at
**`ws://localhost:9001/mqtt`** with the WebSocket subprotocol `mqtt`. There is no
TLS or authentication and no exposed raw MQTT TCP listener. Its configuration is
mounted read-only from `projects/AppHost/configuration/mosquitto.conf`.

This is a generic local integration-test transport, not a MeshCore broker
emulator. It does not implement signed-token authentication, topic ownership,
subscriber roles, payload filtering, topic normalization, or stripping the retain
flag from `/status` messages. Use the actual MeshCore broker when testing those
behaviors. Worker does not connect to this broker yet.

The [reference broker](https://github.com/michaelhart/meshcore-mqtt-broker/tree/4c0496b15a0473045d289527f2ffbe39f73271c5)
uses Aedes 0.51.x, which accepts MQTT 3.1/3.1.1 and supports QoS **0, 1 and 2**.
Its authorization hooks do not cap QoS; a consumer requesting QoS 1 is a client
choice, not a broker restriction. Mosquitto is configured with `max_qos 2` and
default standard QoS delivery (no QoS upgrade). MQTT **3.1.1** is the exercised
compatibility path; Mosquitto additionally supports MQTT 5, unlike that Aedes
version. This setup does not artificially reject MQTT 5.

MQTT state is **in memory**: retained messages and persistent client sessions can
exist during a broker lifetime, but disappear when it restarts. No broker volume
is created. A publisher's broker ACK is not evidence of Worker ingestion or a
database commit.

The published endpoint is host-loopback-only. Local processes and potentially
browser pages can access it without authentication; use synthetic test data only.
Do not expose it to the LAN/internet, bridge it to a real broker, or send private
data. The Aspire resource's running state is not an MQTT protocol health check;
verify readiness with a real MQTT CONNECT/publish/subscribe exchange, not an HTTP
GET health probe against the WebSocket listener.

## Formatting and build checks

CSharpier **1.4.0** is pinned in the repository-local .NET tool manifest. Restore
it once, then use the same formatter for source and supported project files:

```sh
mise exec -- dotnet tool restore
mise exec -- dotnet csharpier format . --include-generated
mise exec -- dotnet csharpier check . --include-generated
mise exec -- dotnet build Pulsas.slnx --no-restore
```

Build outputs, local Aspire/Serena state, and `.tmp` are excluded from formatting.

Drizzle has database write access: treat it as a private local development tool,
not a public endpoint or a production database browser. This setup does not apply
application migrations, copy old data, or start any old application processes.
