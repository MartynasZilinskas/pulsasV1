using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketPayloadConnection(
    ImmutableArray<MeshPacketPayloadEdge> Edges,
    PageInfo PageInfo
);

public record MeshPacketPayloadEdge(
    [property: GraphQLType(typeof(NonNullType<CursorType>))] string Cursor,
    MeshPacketPayload Node
);
