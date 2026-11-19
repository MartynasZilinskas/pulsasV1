using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.GraphQL;

public record MeshPacketConnection(ImmutableArray<MeshPacketEdge> Edges, PageInfo PageInfo);

public record MeshPacketEdge(
    [property: GraphQLType(typeof(NonNullType<CursorType>))] string Cursor,
    MeshPacket Node
);
