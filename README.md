# PULSAS V1

Incremental, reviewed migration from the existing `pulsas` application. The initial
foundation includes Aspire orchestration, PostgreSQL/PostGIS, Drizzle Studio,
and runnable Worker and GraphQL Gateway hosts. No application schema, domain
GraphQL API, MQTT connection, background jobs, Web, replay, or data import is
included yet.

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
