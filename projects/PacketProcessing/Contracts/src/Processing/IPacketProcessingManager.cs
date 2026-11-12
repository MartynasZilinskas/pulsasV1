using System.Collections.Immutable;
using Funcky.Monads;
using Pulsas.PacketProcessing.Contracts.Reports;

namespace Pulsas.PacketProcessing.Contracts.Processing;

public interface IPacketProcessingManager
{
    public record Input(ImmutableArray<byte> OriginalReport, ReportContext Context);

    // In-memory interpretation only; this result does not imply a database commit.
    public record Result(Input Input, PacketReport Report, PacketOutcome Packet);

    Either<ReportError, Result> Process(Input input);
}
