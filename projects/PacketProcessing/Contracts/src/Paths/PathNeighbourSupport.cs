namespace Pulsas.PacketProcessing.Contracts.Paths;

// Copy the source and the endpoints actually used so later catalogue changes cannot change history.
public record PathNeighbourSupport(
    Guid ObservationId,
    Guid SourcePacketId,
    Guid SourceReceptionId,
    Guid SourceContentId,
    string FromKey,
    string ToKey,
    PathNeighbourSupportBasis Basis,
    PathNeighbourSupportSide Side
);
