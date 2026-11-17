using Funcky.Monads;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.Persistence.Receptions;

namespace Pulsas.Persistence.Ingestion;

public interface IReportIngestionManager
{
    record IngestReportInput(
        Guid ReceptionId,
        string BrokerEndpoint,
        IPacketProcessingManager.Result Interpreted
    );

    record IngestReportResult(MeshPacketReception Reception, bool IsDuplicate);

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
