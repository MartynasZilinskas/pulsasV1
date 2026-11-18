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

    enum AssessReceptionError
    {
        MissingReceptionPacket,
    }
}
