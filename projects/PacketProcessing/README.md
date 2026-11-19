# Packet processing

Interprets observer packet reports in memory: packet decoding, ADVERT signatures and
application data. Retains malformed/unsupported content and partial metadata.
Core owns packet interpretation, ingestion, ADVERT node projection, chat projection,
channel reconciliation, and path-assessment policy. Worker durably queues reports
and invokes those operations inside its delivery and startup transactions. Domain
subscriptions are not wired yet.

| Project | Responsibility |
| --- | --- |
| Contracts | Shared data and operation interfaces |
| Core | Packet processing operations and reception, packet/payload, and traffic reads |
| [Persistence](../Persistence/) | EF Core context, storage models, mappings, and migrations |
| GraphQL | Queries and subscriptions for processed results |

Worker owns MQTT input, configuration loading, queue ordering, transactions,
retries, and archival. It passes the same validated channel snapshot to both chat
managers and commits catalogue reconciliation before starting queue consumers.
Gateway hosts GraphQL. Notifications for affected packets are not implemented yet.

Gateway wires the read managers from `Core/src/Paths` to the interfaces in
`Contracts/src/Paths`. The managers query `TrackingDbContext` directly; GraphQL
depends on the contracts, while Persistence owns the EF model and provider mappings.

## Reception path assessment

`Contracts/Paths` owns `IPathAssessmentManager` and immutable assessment records.
`Core/Paths/PathAssessmentManager` owns prefix matching,
catalogue-cache validity, directed neighbour support, reasons, confidence and selection,
as well as unsupported-path and evidence-admission policy.

The manager uses `Persistence/TrackingDbContext` directly through EF Core for one
coherent catalogue/evidence read, shared trie/cache rows, neighbour observations and
immutable JSONB reception snapshots. There is no additional store/repository layer.
Worker supplies the existing context and transaction, retaining grouped ordering,
rollback, retries and archival.

Assessment saving composes optional observation preparation with snapshot staging
and persistence. No advertiser skips only the observation. EF update failures
return the `AssessReceptionError.SaveFailed` union case with the original cause;
cancellation and unexpected exceptions propagate. Delivery maps save failure to a
safe reason and retains its existing bounded retry and rollback policy, without
logging the exception's message.
