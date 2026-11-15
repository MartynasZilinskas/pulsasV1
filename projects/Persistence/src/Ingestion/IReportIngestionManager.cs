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

    enum IngestReportError
    {
        UnknownRegion,
        ConcurrentCreation,
    }

    Task<Either<IngestReportError, MeshPacketReception>> IngestReportAsync(
        IngestReportInput input,
        CancellationToken cancellationToken
    );
}
