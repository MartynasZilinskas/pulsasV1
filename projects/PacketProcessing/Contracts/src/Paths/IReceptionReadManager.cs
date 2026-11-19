using Funcky.Monads;
using NodaTime;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IReceptionReadManager
{
    Task<Option<ReadReceptionResult>> ReadReceptionAsync(
        Guid id,
        CancellationToken cancellationToken
    );

    Task<Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>> ReadPathAssessmentAsync(
        Guid receptionId,
        CancellationToken cancellationToken
    );

    public enum ReadPathAssessmentError
    {
        SnapshotTooLarge,
    }

    public record ReadReceptionResult(
        Guid Id,
        Instant ArrivedAt,
        Instant? HeardAt,
        int? Rssi,
        double? Snr,
        Guid? PacketId,
        ReceptionReadDetails? Details = null
    );

    public record ReadPathAssessmentResult(
        Instant AssessedAt,
        string PolicyVersion,
        PathAssessmentResult Result
    );
}
