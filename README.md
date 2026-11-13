# PULSAS V1

Incremental migration of Pulsas. Includes a MeshCore codec library, local
infrastructure, Worker/Gateway foundations, and an observer traffic generator.
Worker interprets packet reports in memory. Persistence models and startup
configuration are migrated; storage writes, chat linking, path assessment and
domain GraphQL queries are not wired yet.

The [packet-processing project structure](projects/PacketProcessing/README.md)
separates Contracts, Core, GraphQL, and Persistence.

Worker supports the previous project's startup secret-file loader and validates
configured chat channels and routing scopes. See [configuration](configuration/README.md)
and [persistence](projects/Persistence/README.md) for current integration limits.

## Run

Requires mise, .NET **11.0.100**, Podman, and a trusted ASP.NET Core development
certificate. Aspire **13.6.5** is pinned; no password or runtime exports are needed.

```sh
./aspire.sh
```

| Tool | Address |
| --- | --- |
| Aspire dashboard | https://localhost:17241 — use the private login link in the terminal |
| Drizzle Studio | http://localhost:4983 |
| GraphQL IDE | http://localhost:5100/graphql/ui/ |
| MQTT WebSocket | ws://localhost:9001/mqtt |
| Traffic status | http://localhost:5300/traffic/status |

GraphQL currently exposes only `serviceInfo { name role }`. Worker's `/health`
also checks its MQTT subscription; `/alive` remains process-only. These
checks do not establish domain ingestion readiness. Services are local development
tools, not public deployments.

Ctrl+C stops the stack. PostgreSQL and Drizzle data stay in
`pulsas-v1-postgres-data` and `pulsas-v1-drizzle-data`. Keep the AppHost's
`pulsas-v1-apphost` user-secrets store with the PostgreSQL volume: changing its
password does not update an initialized database. Do not delete volumes to fix
authentication. Drizzle has database write access.

## Generate observer traffic

The generator connects to MQTT but starts **idle**, publishing nothing. In the
Aspire dashboard, open the `traffic-generator` resource actions and choose
**Start traffic** or **Stop traffic**.
You can also use:

```sh
curl -X POST http://localhost:5300/traffic/start
curl http://localhost:5300/traffic/status
curl -X POST http://localhost:5300/traffic/stop
```

Each run creates fresh synthetic observer and sender identities, publishes online
status, then reports signed ADVERT packets through every observer. It publishes
offline status on clean completion/stop. No messages are retained. This imitates
observer reports, not radio propagation or repeater forwarding.

Defaults: **3 observers, 60 rounds, 1 second between rounds, QoS 1**. A round is
one signed packet reported by every observer (180 packet reports by default).
Change these through `TrafficGenerator__ObserverCount`, `TrafficGenerator__Rounds`,
`TrafficGenerator__IntervalMilliseconds`, `TrafficGenerator__Qos`, and
`TrafficGenerator__Region`. The endpoint is `TrafficGenerator__Endpoint`; Aspire
supplies its local broker. Configuration is read at host startup.

Start is idempotent while a run is active. Stop finishes offline-status cleanup
and leaves MQTT connected; host shutdown disconnects. Finished runs can be started
again. Status shows connection state,
run state, completed rounds, and completed publish counts—not Worker ingestion
or database commits. Transport failures end the run; there is no reconnect loop.
Connection failure is visible in status; Start retries a disconnected client.
The control endpoints exist only in Development.

## See Worker receive messages

After restarting Aspire with these changes, wait for `worker` to become healthy,
then start traffic. Open **worker → Logs** in the Aspire dashboard:

```text
Dummy MQTT consumer subscribed to meshcore/VNO/+/status and meshcore/VNO/+/packets
Dummy MQTT received meshcore/VNO/<observer>/packets: ... bytes, QoS AtLeastOnce, retained False
Packet report interpreted in memory: observer ..., region VNO, packet Decoded, type Advert, payload Advert, advertiser ..., signature Verified, metadata Decoded; not stored
```

This Development-only consumer logs receipt metadata and interprets packet
reports: validates observer/topic consistency, decodes packets and verifies ADVERT
signatures while retaining partial metadata. Status messages remain receipt-only.
Observer claims are not authenticated. Invalid reports are rejected; malformed
packets and unknown payloads remain interpretation outcomes. No reports are stored,
deduplicated, or notified to GraphQL. Logs omit raw report bytes and chat text.
It requests QoS 1, retries connection/subscription failures every five seconds,
and resubscribes after disconnects. Messages sent while disconnected can be lost;
QoS 1 can deliver duplicates. Worker `/health` is unhealthy until subscribed.

Aspire supplies `DummyMqtt__Endpoint`. For standalone development it defaults to
`ws://localhost:9001/mqtt`; `DummyMqtt__Region` defaults to `VNO`. If you change the
generator region, set the Worker region to match. No consumer runs in Production.

## MQTT and MeshCore

Mosquitto **2.0.22** is anonymous, unencrypted, loopback-only, and in-memory.
Use synthetic data only. MQTT **3.1.1** and QoS **0–2** are supported by the
generator. Mosquitto also accepts MQTT 5. The
[reference MeshCore broker](https://github.com/michaelhart/meshcore-mqtt-broker/tree/4c0496b15a0473045d289527f2ffbe39f73271c5)
has no QoS 1 cap; its authentication, authorization, filtering, and `/status`
retain handling are **not** reproduced here. Use that broker for those behaviors.

The standalone [MeshCore library](libraries/Pulsas.MeshCore/README.md) contains
packet encoding/decoding and payload codecs. It has no MQTT, hosting, or database dependency.

## Check changes

```sh
mise exec -- dotnet tool restore
mise exec -- dotnet restore Pulsas.slnx
mise exec -- dotnet csharpier format . --include-generated
mise exec -- dotnet csharpier check . --include-generated
mise exec -- dotnet build Pulsas.slnx --no-restore
mise exec -- dotnet test Pulsas.slnx --no-build --no-restore
```

CSharpier **1.4.0** is pinned. Codec tests include independent cryptographic
vectors and malformed-input cases. Broker integration tests are opt-in; see
the [generator](tests/Pulsas.TrafficGenerator.Tests/README.md) and
[Worker](tests/Pulsas.Worker.Tests/README.md) test instructions.
