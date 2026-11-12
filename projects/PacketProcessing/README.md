# Packet processing

Interprets observer packet reports in memory: packet decoding, ADVERT signatures and
application data. Retains malformed/unsupported content and partial metadata.
No storage, chat linking, path assessment or domain subscriptions yet.

| Project | Responsibility |
| --- | --- |
| Contracts | Shared data and operation interfaces |
| Core | Decoding, chat linking, evidence updates and path assessment |
| [Persistence](../Persistence/) | Database storage, transactions and change notifications |
| GraphQL | Queries and subscriptions for processed results |

Worker hosts MQTT input; Gateway hosts GraphQL. Transactional processing and
notifications for all affected packets are the next stages.
