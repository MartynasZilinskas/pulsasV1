# PULSAS V1

Incremental, reviewed migration from the existing `pulsas` application. The initial
slice contains local Aspire orchestration only: PostgreSQL/PostGIS and Drizzle
Studio. No Worker, Gateway, Web, application schema, MQTT connection, replay, or
data import is included.

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

Drizzle has database write access: treat it as a private local development tool,
not a public endpoint or a production database browser. This setup does not apply
application migrations, copy old data, or start any old application processes.
