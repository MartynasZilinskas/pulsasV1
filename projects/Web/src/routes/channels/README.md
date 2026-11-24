# Channels fixture workspace

The channel directory, message history, and packet relationships are synthetic
examples in `channel-fixtures.ts`. They are not a GraphQL response model. The page
is read-only: there is no send action, subscription, or Gateway connection.

Each fixture message represents one transmission: its path variants retain the
same payload type and transport. This fixture convention is not a constraint on
the backend's broader chat-message grouping by channel and canonical text/time.

## Current V1 GraphQL boundary

`PacketProcessing/GraphQL/src/PacketPayloadTypes.cs` exposes
`GroupTextPayload.channelHash` and nullable `chatMessage { id }`. The linked
message is included only for an active, public channel. The current registered
query surface has no channel directory or channel-message history operation.

Packet/reception queries expose packet identifiers and hashes, route type,
path-prefix width, nullable path hops, arrival times, and observer identities.
An empty path differs from an unavailable path. Path prefixes are not resolved
node identities. A sender name, when eventually exposed, remains a claimed name,
not a verified node identity.

The following fixture fields need additional GraphQL reads before live wiring:

- Channel name, kind, directory ordering, and latest-message metadata.
- Message text, claimed sender name, sender-clock timestamp, and first arrival.
- Paginated channel history and message-to-exact-packet relationships.
- Message-wide packet-variant and distinct-observer aggregates. Counts must not
  be derived from a partially loaded page of packets or receptions.

`Persistence/src/Chat/Channel.cs` and `ChatMessage.cs` store some of these fields,
but storage does not make them part of the API. The older `pulsas` application
has richer chat queries; those are not registered in this V1 Gateway.

Keep fixture-only gaps at this boundary. Future API integration should preserve
public/active visibility rules, nullable lookups, cursor pagination, distinct
sender/arrival clocks, and exact-packet versus reception identity.
