using System.Collections.Immutable;
using Funcky.Monads;
using Pulsas.MeshCore.Packets;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.PacketProcessing.Core.Interpretation;
using Pulsas.PacketProcessing.Core.Reports;

namespace Pulsas.PacketProcessing.Core.Processing;

public class PacketProcessingManager : IPacketProcessingManager
{
    public Either<ReportError, IPacketProcessingManager.Result> Process(
        IPacketProcessingManager.Input input
    )
    {
        return ObserverReportDecoder
            .Decode(input)
            .Select(report => new IPacketProcessingManager.Result(
                input,
                report,
                DecodePacket(report.RawHex)
            ));
    }

    private static PacketOutcome DecodePacket(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return new PacketOutcome.Absent();
        }
        ImmutableArray<byte> raw;
        try
        {
            raw = [.. Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal))];
        }
        catch (FormatException)
        {
            return new PacketOutcome.InvalidHex();
        }
        return PacketCodec
            .Decode(raw.AsSpan())
            .Match<PacketOutcome>(
                left: error => new PacketOutcome.Malformed(raw, error),
                right: packet => new PacketOutcome.Decoded(
                    raw,
                    packet,
                    AdvertInterpreter.Interpret(packet)
                )
            );
    }
}
