using NodaTime;

namespace Pulsas.PacketProcessing.GraphQL;

public record MeshTrafficTrendBucket(
    Instant From,
    Instant To,
    long ReceptionCount,
    long WirePacketCount,
    long SharedPayloadCount,
    MeshTrafficSignalSummary Snr,
    MeshTrafficSignalSummary Rssi
);
