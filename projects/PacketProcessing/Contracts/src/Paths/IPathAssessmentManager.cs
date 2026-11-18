using Funcky;
using Funcky.Monads;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IPathAssessmentManager
{
    Task<Either<AssessReceptionError, Unit>> AssessReceptionAsync(
        AssessReceptionInput input,
        CancellationToken cancellationToken
    );

    record AssessReceptionInput(Guid ReceptionId);

    union AssessReceptionError(
        AssessReceptionError.MissingReceptionPacket,
        AssessReceptionError.SaveFailed
    )
    {
        public record MissingReceptionPacket;

        public record SaveFailed(Exception Cause)
        {
            public override string ToString() => nameof(SaveFailed);
        }
    }
}
