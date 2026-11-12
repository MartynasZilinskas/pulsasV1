using System.Collections.Immutable;
using Pulsas.MeshCore.Packets;

namespace Pulsas.PacketProcessing.Contracts.Processing;

public abstract record PacketOutcome
{
    public record Absent : PacketOutcome;

    public record InvalidHex : PacketOutcome;

    public record Malformed(ImmutableArray<byte> Raw, PacketDecodeError Error) : PacketOutcome;

    public record Decoded(
        ImmutableArray<byte> Raw,
        MeshCorePacket Packet,
        PayloadInterpretation Interpretation
    ) : PacketOutcome;
}
