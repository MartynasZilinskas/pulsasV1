# Packet processing

Interprets observer packet reports in memory: packet decoding, ADVERT signatures and
application data. Retains malformed/unsupported content and partial metadata.
Core owns packet interpretation and path-assessment policy. Worker durably queues
reports and persists interpreted evidence through EF Core. Worker links configured
GroupText chat and invokes path assessment inside content delivery. Domain
subscriptions are not wired yet.

| Project | Responsibility |
| --- | --- |
| Contracts | Shared data and operation interfaces |
| Core | Decoding, chat linking, evidence updates and path assessment |
| [Persistence](../Persistence/) | Database storage, transactions and change notifications |
| GraphQL | Queries and subscriptions for processed results |

Worker hosts MQTT input, transactional ingestion and grouped ADVERT/GroupText processing;
Gateway hosts GraphQL. Notifications for affected packets are not implemented yet.

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
