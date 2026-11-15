# Packet processing

Interprets observer packet reports in memory: packet decoding, ADVERT signatures and
application data. Retains malformed/unsupported content and partial metadata.
Core is storage-free. Worker durably queues reports and persists interpreted evidence
through Persistence; chat linking, path assessment and domain subscriptions are not wired yet.

| Project | Responsibility |
| --- | --- |
| Contracts | Shared data and operation interfaces |
| Core | Decoding, chat linking, evidence updates and path assessment |
| [Persistence](../Persistence/) | Database storage, transactions and change notifications |
| GraphQL | Queries and subscriptions for processed results |

Worker hosts MQTT input, transactional ingestion and grouped ADVERT processing;
Gateway hosts GraphQL. Notifications for affected packets are not implemented yet.
