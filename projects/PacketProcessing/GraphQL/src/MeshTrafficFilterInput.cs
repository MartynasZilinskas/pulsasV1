namespace Pulsas.PacketProcessing.GraphQL;

public record MeshTrafficFilterInput(
    [property: GraphQLType(typeof(ListType<NonNullType<IdType>>))]
        IReadOnlyList<string>? ObserverIds,
    IReadOnlyList<string>? RegionCodes,
    IReadOnlyList<LegacyPacketType>? PacketTypes
);
