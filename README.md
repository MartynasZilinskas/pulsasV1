# PULSAS V1

Incremental migration of Pulsas. Includes a MeshCore codec library, local
infrastructure, Worker/Gateway foundations, and an observer traffic generator.
Local Aspire runs build a [PostGIS + PGMQ image](projects/AppHost/containers/postgres/README.md);
deployment will use the provider's image. PGMQ transaction/ordering tests are
opt-in. Development Worker durably queues MQTT reports, persists packet/content/reception
evidence, and processes verified ADVERT metadata and configured GroupText chat through grouped content work. Aspire applies the persistence schema
through the `database-setup` resource; PostGIS maintains cached node-region
assignments. Grouped content work records point-in-time path assessments per
reception. Gateway can expose those snapshots for local development.

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
| GraphQL IDE (opt-in) | http://localhost:5100/graphql/ui/ |
| MQTT WebSocket | ws://localhost:9001/mqtt |
| Traffic status | http://localhost:5300/traffic/status |

The GraphQL endpoint and IDE are disabled by default. See
[Reception detail in GraphQL](#reception-detail-in-graphql) for the local opt-in.
Worker's `/health` also checks its MQTT subscription; `/alive` remains process-only. These
checks do not establish domain ingestion readiness. Services are local development
tools, not public deployments.

Ctrl+C stops the stack. PostgreSQL and Drizzle data stay in
`pulsas-v1-postgres-data` and `pulsas-v1-drizzle-data`. Keep the AppHost's
`pulsas-v1-apphost` user-secrets store with the PostgreSQL volume: changing its
password does not update an initialized database. Do not delete volumes to fix
authentication. Drizzle has database write access.

### Local Web foundation

The React/Vite project in `projects/Web` uses the Node version pinned in `mise.toml`.
Install its locked dependencies before starting Aspire; AppHost does not install them.

```sh
(cd projects/Web && mise exec -- npm ci --ignore-scripts)
./aspire.sh run -- --Web:ShowcaseOnly=true
```

Aspire starts `web` at `http://127.0.0.1:5173` in either run mode. The command above
starts only Web and the Aspire dashboard, without PostgreSQL, migrations, or MQTT.
Open [Kitchen Sink](http://127.0.0.1:5173/kitchen-sink) to try node roles and
modifiers, compare compact packet and route badges, inspect packet paths with
seven indicator styles, and browse channel icons and the mesh logo. In Packet
traffic, zoom into a time range to update both the histogram and table. Open a
row to view the reception details, or scroll the table for older receptions.
The full-range table also demonstrates live feed markers with sample events.
Map examples reproduce the legacy node states, path evidence, and packet flow
using a 20 November 2026 [Beacon](https://beacon.atvirastinklas.lt) snapshot: the
six nearest located KUN nodes within 100 km of the Kaunas showcase center, plus
one node without a location. Node names, public keys, and advertised coordinates
are preserved. Role specimens, candidate matches, and packet activity are
generated examples, not reception evidence. The MapLibre map uses a bundled
worker and a schematic background; it requests no external map tiles or registry
data. The showcase does not connect to Gateway.
The local Web resource is not published.

With the Web resource running, use `mise exec -- npm run test:browser` from
`projects/Web` to check startup, keyboard and touch interaction, and UI SDK agent
actions. The tests use local Chromium (`/usr/bin/chromium` by default, or
`CHROMIUM_EXECUTABLE`), without downloading a browser. Set
`PULSAS_WEB_TEST_PORT` to test an isolated Vite development instance on another
loopback port.

## Reception detail in GraphQL

To enable read-only reception lookup on a trusted local development instance:

```sh
./aspire.sh run -- --Gateway:EnableReceptionReads=true
```

AppHost supplies the database connection and waits for migrations. For standalone
development Gateway, set `Gateway__EnableReceptionReads=true` and supply a migrated
`ConnectionStrings__pulsas` through the normal configuration path. The opt-in
serves GraphQL reads only on loopback via JSON POST and rejects foreign host and
browser origin headers. Use a loopback listener for standalone development; do
not expose it via a reverse proxy. Outside Development, Gateway does not expose
GraphQL or require a database connection.

Use `/graphql/ui` or POST the following operation to `/graphql` with a reception
UUID (not a packet UUID) as the `id` variable:

```graphql
query ReceptionDetail($id: ID!) {
  meshPacketReceptionById(id: $id) {
    ... on MeshPacketReception {
      id
      packetId
      arrivedAt
      heardAt
      pathAssessment {
        assessedAt
        policyVersion
        pathKind
        status
        hops {
          position
          prefix
          outcome
          selectedCandidate { nodeId publicKey confidence reasons supportCount }
          candidates(first: 8) { items { publicKey confidence } totalCount hasMore }
        }
      }
    }
    ... on MeshPacketReceptionNotFoundError { message }
  }
}
```

The reception, wire-packet, shared-payload, and observer activity lookups require
an ID and return a union of the requested data and a typed not-found result.
A valid but unknown UUID returns the not-found result in `data`. Malformed UUIDs
return `INVALID_INPUT` in GraphQL `errors`, not a not-found result. An existing
reception without a recorded assessment (including older and pending receptions)
returns `pathAssessment: null`. Unresolved and unsupported paths are recorded
results, not missing assessments. Results are never recomputed on read.
Candidate pages use `offset` in stored order, with `first` from 1 to 20; the
selected candidate is returned independently of paging. The 4 MiB snapshot read
limit produces an explicit `PATH_ASSESSMENT_TOO_LARGE` error, not a missing result.

The legacy reception browser operations also work: `meshPacketReceptions`
supports observer, region, packet type and arrival-time filters;
`meshPacketReceptionHistogram` supports the same filters and a refresh cursor;
`observers` and `regions` populate the filter selectors. Reception list and detail
include packet/payload, observer, broker and region summaries. Broker labels omit
URL paths and query strings. GroupText links include only the ID of a message in
an active public channel.

GraphQL region codes are plain strings. If a client declares region-code
variables with the old `IATA` scalar, change them to `String`, preserving any
list and non-null modifiers (for example, `[IATA!]` becomes `[String!]`). Region
codes must be nonempty; case and whitespace are preserved.

Browser cursors are scoped to their collection and filters and expire on Gateway
restart. Restart pagination without a cursor after a restart. If a region code
is too long to fit in a cursor, changing or deleting that region also invalidates
its cursor. The histogram
defaults to a rolling 24-hour window and has a 30-day lookback; that limit does
not change what the reception list or detail can read. As in the legacy read
API, a GraphQL operation can select at most four distinct root response fields,
including fields selected through aliases or fragments.

Packet and node catalogue queries, standalone broker queries, chat browsing and
subscriptions are not part of this slice. Candidate support is summarized by
stored reasons and counts; raw packets and support source records are not exposed.

### Traffic summary

`meshTrafficSummary(from: Instant!, to: Instant!, filter: MeshTrafficFilterInput)`
requires explicit server-arrival bounds. The interval includes `from` and excludes
`to` and may be up to 30 days long. Clients resolve presets to explicit instants.
Optional filters match observer IDs, exact region codes, and packet types.

The summary counts stored receptions, distinct wire packets, and distinct encoded
payloads separately; none is a count of physical transmissions.
`activeObserverCount` and `activeRegionCount` count distinct observers and exact
region codes among matching receptions, not currently online observers or
configured regions. `packetTypeCounts` partitions matching receptions by stored
packet type; the final `null` type, when present, counts receptions without a
linked packet. Only types with matching receptions are listed. SNR and RSSI
each report their own sample count and a mean, which is `null` without samples.
The mean gives each valid reading equal weight; it is not an average of bucket
means. SNR uses PostgreSQL numeric accumulation of the stored doubles' round-trip
decimal values so the intermediate sum can exceed the double range.
The tag list groups matching payloads and their receptions by current stored tag.
It is empty when no tags match; groups can overlap and should not be added to
the overall totals. History coverage is `UNKNOWN`, including for empty windows.
Later ingestion can change a previously queried window.

### Traffic and signal trends

`meshTrafficTrend(from: Instant!, to: Instant!, filter: MeshTrafficFilterInput)`
accepts the same window and filters as the summary. It picks the smallest supported
UTC bucket width that fits the window in at most 120 buckets and returns it as
`intervalSeconds`. The first and last bucket are clipped to the exact requested
bounds. Each bucket reports receptions, distinct wire packets, distinct encoded
payloads, and independent SNR and RSSI sample counts and averages.

Empty buckets have zero stored counts and `null` averages. Coverage is always
`UNKNOWN`; an empty bucket does not establish that the mesh was quiet. Use the
summary for window-wide distinct totals rather than adding bucket counts. Each
trend is one consistent read; a separate summary may see newer data.

### Frame-size and signal distributions

`meshTrafficDistributions(from: Instant!, to: Instant!, filter: MeshTrafficFilterInput)`
uses the same exact bounds and filters. `frameSizeBytes` counts one sample per
distinct linked wire packet, measured from the stored frame bytes. `snr` and
`rssi` count valid samples per matching reception, including an unlinked reception
unless a packet-type filter excludes it. Missing values and non-finite SNR are
not samples. The three histograms use one database snapshot.

Each histogram includes its own sample count and all buckets, even empty ones.
Buckets include their lower edge and exclude their upper edge; a `null` edge
means unbounded on that side. Finite edges are 32–256 bytes in steps of 32,
−30–20 dB in steps of 5, and −130–−30 dBm in steps of 10. These are grouping
edges, not validity limits. Coverage remains `UNKNOWN`.

### Observer activity

`observerActivitySummary(observerId: ID!, from: Instant!, to: Instant!, filter: ObserverActivityFilterInput)`
uses the same exact server-arrival bounds, up to 30 days. Pass the observer UUID
from `observers`; optional `regionCodes` and `packetTypes` filter the receptions
before all calculations. The response includes total receptions, the packet-type
mix, SNR and RSSI means with independent valid sample counts, and the first and
last matching server arrivals. Its UTC buckets report reception counts, including
zero for empty buckets, and are clipped to the requested bounds. All fields come
from one database snapshot.

Select activity fields with `... on ObserverActivitySummary`. An unknown observer
returns `ObserverNotFoundError`. A known observer with no matching receptions
returns zero counts and `null` first/last arrivals and means.
`historyCoverage` is `UNKNOWN` in either a populated or empty summary; gaps in
recorded activity do not establish downtime.

### Observed flood overlap

`observerFloodOverlap(observerAId: ID!, observerBId: ID!, from: Instant!, to: Instant!, filter: ObserverActivityFilterInput)`
compares two distinct observer UUIDs in the exact `[from, to)` server-arrival
window, up to 30 days. Optional `regionCodes` match stored receiving regions
exactly (including case and whitespace); `packetTypes` narrows the eligible
packets. Only linked wire packets with `Flood` or `TransportFlood` routing count.

Select `onlyACount`, `onlyBCount`, `bothCount`, and `unionCount` with
`... on ObserverFloodOverlap`. A packet counts once per observer regardless of
repeated reports. Different encoded paths remain distinct even if their payload
matches. The first three counts partition the union. “Only” means only among
the two observers' qualifying stored reports; it does not indicate packet loss,
missed reception, or observer downtime.

Known observers with no qualifying packets return four zeroes. Successful
results always report `historyCoverage: UNKNOWN`, even when populated. An
unknown observer returns `OverlapObserversNotFoundError` with `missingObserverIds`
in argument order, not a partial comparison. Malformed or equal IDs and invalid
windows return GraphQL errors.

### Grouped packets

Use `meshPacketReceptions` for one row per reception, or `meshPacketPayloads`
for one row per encoded payload type, version, and byte sequence with at least
one linked reception. The payload feed is ordered by `firstArrivedAt` descending.
A later reception updates the counts and `lastArrivedAt` without moving the
payload to the top; an earlier-arriving backfill can change its position. Counts
are exact stored packets and receptions, not transmissions or distinct observers.
Arrival times are server arrival times; the radio's `heardAt` remains nullable.

```graphql
query GroupedPackets($after: Cursor) {
  meshPacketPayloads(first: 20, after: $after) {
    edges {
      node {
        id payloadType payloadVersion firstArrivedAt lastArrivedAt
        packetCount receptionCount
        decodedPayload { type ... on DecodedAdvertPayload { name publicKey } }
      }
    }
    pageInfo { hasNextPage endCursor }
  }
}
```

`meshPacketPayloadById` exposes the merged `receptions` timeline and the exact
wire `packets` in packet ID order. Different encoded paths are different packets;
repeats of the exact wire bytes share a packet. `firstPacket` is the packet from
the earliest retained reception, with ties broken by packet ID. It is the
default packet to display, not evidence of the original transmission. Backfill or
deletion can change the representative. A payload with no linked receptions is
omitted from the feed and its lookup returns `MeshPacketPayloadNotFoundError`.

```graphql
query FirstPacketHistory($id: ID!, $first: Int!, $after: Cursor) {
  meshPacketPayloadById(id: $id) {
    ... on MeshPacketPayload {
      firstPacket {
        id
        pathHops { prefix }
        receptions(first: $first, after: $after) {
          edges { node { id arrivedAt heardAt rssi snr observer { id } } }
          pageInfo { hasNextPage endCursor }
        }
      }
    }
    ... on MeshPacketPayloadNotFoundError { message }
  }
}
```

Both histories return `MeshPacketReception` nodes. Use the reception `id` to
open its stored assessment. Connections are live reads, not snapshots, and
each cursor is scoped to its connection and parent. If `firstPacket` changes,
restart its history without the old packet's cursor.

Use `meshPacketById` to reopen a selected packet with just its packet UUID:

```graphql
query PacketDetail($id: ID!, $after: Cursor) {
  meshPacketById(id: $id) {
    ... on MeshPacket {
      id
      pathHops { prefix }
      receptions(first: 20, after: $after) {
        edges { node { id arrivedAt snr rssi } }
        pageInfo { hasNextPage endCursor }
      }
    }
    ... on MeshPacketNotFoundError { message }
  }
}
```

A packet can have an empty reception history and still be visible if another
packet sharing its payload has a retained reception. Once the payload has none,
the lookup returns `MeshPacketNotFoundError`, just as for an unknown UUID.
Fetch detail and history on demand. `MeshPacket.sharedPayload` links back to the
shared payload; legacy `MeshPacket.payload` is unchanged. The shared decode
excludes packet verification results and chat message links.

Packet statistics are exact. On the reception feed, even one requested packet
statistic can require counting that packet's retained history. The Gateway's
field budget limits fan-out, not the number of stored rows scanned. Prefer
loading statistics on demand; high-volume reads have not been qualified.

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
Synthetic locations cycle through KUN, PLQ, SQQ and VNO. These demo coordinates
only receive region assignments when the corresponding shapes already exist in
the database; startup does not import or replace regions.

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
```

The Development-only MQTT callback acknowledges packet reports after durable
acceptance into PGMQ `ingestion`. An ingestion consumer validates observer/topic
consistency and stores deduplicated packets/content plus a distinct reception for
each accepted report. In the same transaction it enqueues `packet_content` work
and archives ingestion. Content work groups by type, payload version and full
payload hash; node projection and work completion commit together.
Status messages remain receipt-only; observer claims are not authenticated.
Invalid reports and exhausted retries go to `ingestion_quarantine` with their
original envelopes and reasons. Malformed packets remain reception evidence;
unknown content remains opaque. Verified ADVERTs upsert
nodes by public key with strictly newer signed timestamps; duplicates/equal timestamps
do not churn metadata. See the persistence README for omission/reset and clock rules.
Original reports remain in queue archives. No domain events are notified to GraphQL.
Observer identity is taken only from the MQTT topic; JSON `origin_id` is ignored.
Receptions deduplicate by observer, raw bytes, reported timestamp, RSSI and SNR;
duplicate deliveries log their existing reception ID and incoming transport context
without scheduling content processing again. Region IDs are operator-defined,
subject only to MQTT topic-segment limits at ingress. See the persistence README
for timestamp precision and historical-row limitations.
GroupText uses configured channel keys; exactly one successful channel decode is
required. Unknown, malformed and ambiguous matches stay opaque. Chat messages
deduplicate by channel, message timestamp and exact decoded text, excluding the
attempt counter and packet path; all packets/receptions remain separate evidence.
Sender prefixes remain unverified text, not authenticated node identities.
Logs omit raw report bytes and chat text. Queue archives/quarantine contain report data
and require database-level access protection; no automated retention or replay is provided.
It requests QoS 1, retries connection/subscription failures every five seconds,
and resubscribes after disconnects. Messages sent while disconnected can be lost;
QoS 1 can deliver duplicates. Worker `/health` is unhealthy until subscribed.

Aspire supplies `DummyMqtt__Endpoint`. For standalone development it defaults to
`ws://localhost:9001/mqtt`; `DummyMqtt__Region` defaults to `VNO`. If you change the
generator region, set the Worker region to match. No consumer runs in Production.
Standalone Worker also requires `ConnectionStrings:pulsas` for the migrated database;
Aspire injects it and waits for migrations before starting Worker.
`ReportDelivery` configures `MaximumAttempts` (5), `RetryDelaySeconds` (5) and
`PollDelayMilliseconds` (250). Queue names are fixed to the migration-provisioned
topology; application configuration does not override them. Tests explicitly inject
isolated queues. No per-node ADVERT queue is used.

Before consumers start, Worker atomically reconciles configured channels. Removed
channels become inactive and non-public; historical messages and links remain.
Workers sharing the database must use the same authoritative startup configuration.
Channel keys remain in memory and are never persisted. Configuration changes do not
automatically replay archived content or reassign existing message links.
No channels are enabled by default: select a channel configuration through
`PULSAS_SECRETS_PATH` as described in [configuration](configuration/README.md).

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

After the final edits, run Slopwatch from the repository root:

```sh
dotnet tool run slopwatch -- analyze --fail-on warning \
  --exclude '.tmp/**,.aspire/**,.repowise/**,.serena/**,.claude/**,.codex/**,.cursor/**,.kiro/**,**/TestResults/**'
```

The pinned local tool uses `.slopwatch/baseline.json` to distinguish existing
findings from regressions. Fix new findings rather than updating the baseline
to pass the check.

CSharpier **1.4.0** is pinned. Codec tests include independent cryptographic
vectors and malformed-input cases. Broker integration tests are opt-in; see
the [generator](tests/Pulsas.TrafficGenerator.Tests/README.md) and
[Worker](tests/Pulsas.Worker.Tests/README.md) test instructions.
