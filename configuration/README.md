# Worker configuration

Set `PULSAS_SECRETS_PATH` to one JSON file or a directory's immediate `*.json`
files. Restart to apply changes. `chat.json` is shipped in Worker build/publish
output but is **not loaded automatically**. YAML is not supported.

```sh
PULSAS_SECRETS_PATH="$PWD/configuration/chat.json" mise exec -- dotnet run --project projects/Worker.ApiService
```

```json
{
  "chat": {
    "standard_public": true,
    "hashtags": ["test", "#emergency"],
    "explicit": {
      "team": {"name": "Team", "key": "<Base64 secret>", "public": false}
    }
  },
  "scopes": [
    {"name": "eu"},
    {"name": "$team", "key": "<Base64 16-byte secret>"}
  ],
  "regions": {
    "east": {
      "name": "Rytų Lietuva", "country": "LT",
      "center": [55.03610755, 25.6102797],
      "border_file": "/etc/pulsas/borders/east.geojson"
    }
  },
  "region_groups": {
    "lithuania": {"name": "Lietuva", "regions": ["east"], "center": [55.2, 23.75], "zoom": 6}
  },
  "default_view": {"region_group": "lithuania"}
}
```

- Standard public defaults off; inclusion enables other channels. Publication
  defaults true; explicit-key entries may override it. Explicit map keys are stable
  channel identities, not wire hashes. Keys contain 16 or 32 bytes; keep them secret.
- Public chat/scope names accept one optional `#`, normalized before duplicate
  checks and key derivation. Case/spelling are preserved; no trimming. `$` scopes
  retain their names and require 16-byte keys. Routing is not geography.
- Regions use operator-defined IDs, not enforced IATA codes. Country is a two-letter
  uppercase code; centers are `[latitude, longitude]`. Regions can stand alone or
  belong to multiple groups. Region/group zoom is optional and nonnegative.
- `default_view` must reference exactly one `region` or `region_group`. Omit it for
  an all-regions overview. No frontend/API delivery is wired yet.

Each top-level section may belong to only one selected file. Worker treats
`chat.hashtags`, `chat.explicit`, `scopes`, `regions`, `region_groups`, and
`default_view` as complete declarations: a later file/in-memory provider replaces
the earlier catalogue. Omission inherits; empty arrays/maps or null clear a
catalogue (an empty default view is invalid). Environment variables and CLI patch
individual fields afterward, with CLI last. For example,
`chat__explicit__team__public=false` overrides publication without replacing keys.
Do not use environment/CLI array overrides to remove entries. Map keys follow
.NET configuration's case-insensitive, colon-delimited key semantics.

Old `Chat:Channels` and `Transport:Scopes` formats fail startup with a migration
error. Unknown fields within the owned sections are rejected.

### Content-processing concurrency

`ReportDelivery:ContentConcurrency` defaults to **4** and must be positive.
Set `ReportDelivery__ContentConcurrency=8` to run eight content-processing tasks
inside the single Worker process. MQTT still uses one connection, and report
ingestion still has one ordered consumer. Each content task obtains its own DI
scope and database transaction; PGMQ grouped-head locking keeps matching content
sequential while different contents can proceed concurrently. The count is a
limit, not a guaranteed throughput increase; database connections and contention
also constrain useful parallelism. Restart Worker to apply changes.

Worker reconciles channel identities and visibility atomically before queue consumers
start. Configured channels are active; removed channels become inactive and
non-public without deleting historical messages. Standard public, hashtag-derived
and explicit keys are tried for GroupText; one successful configured channel is
required, not merely a matching one-byte wire hash. Duplicate-key aliases are
ambiguous and do not create a message. Keys and plaintext are never logged.
Channel keys stay in memory, not in the database. Workers sharing the database
must use the same authoritative startup snapshot; conflicting snapshots are not
supported. Existing packet links are not reassigned when configuration changes;
archived content is not replayed automatically.

Border paths are retained as supplied, not opened or imported; use absolute paths
for future import. Scope reassessment, configured geography persistence and
operational retention policies are not wired. Persisted region constraints still require the
separately approved model change for operator-defined IDs. Aspire now applies the
persistence schema and cached node-geography triggers, but does not import this
configuration into that database; see [persistence](../projects/Persistence/README.md).
