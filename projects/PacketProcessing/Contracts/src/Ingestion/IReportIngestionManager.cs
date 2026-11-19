using System.Collections.Immutable;
using Funcky.Monads;
using Pulsas.MeshCore.Packets;
using Pulsas.PacketProcessing.Contracts.Processing;

namespace Pulsas.PacketProcessing.Contracts.Ingestion;

public interface IReportIngestionManager
{
    record IngestReportInput(
        Guid ReceptionId,
        string BrokerEndpoint,
        IPacketProcessingManager.Result Interpreted
    );

    record IngestReportResult(
        Guid ReceptionId,
        bool IsDuplicate,
        Option<PacketContentIdentity> Content
    );

    record PacketContentIdentity(
        PacketType Type,
        short PayloadVersion,
        ImmutableArray<byte> PayloadHash
    );

    enum IngestReportError
    {
        UnknownRegion,
        ConcurrentCreation,
    }

    Task<Either<IngestReportError, IngestReportResult>> IngestReportAsync(
        IngestReportInput input,
        CancellationToken cancellationToken
    );
}
