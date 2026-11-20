namespace Pulsas.PacketProcessing.GraphQL;

public record ObserverActivityFilterInput(
    IReadOnlyList<string>? RegionCodes,
    IReadOnlyList<LegacyPacketType>? PacketTypes
);
