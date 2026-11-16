using Funcky;
using Funcky.Monads;

namespace Pulsas.Worker.ApiService.Chat;

public interface IGroupTextManager
{
    record ProcessGroupTextInput(Guid PacketId);

    enum ProcessGroupTextError
    {
        PacketNotFound,
        MissingChannel,
        ConflictingPacketLink,
        ConcurrentCreation,
    }

    // Caller owns rollback on Left and commits projection together with queue completion.
    Task<Either<ProcessGroupTextError, Unit>> ProcessGroupTextAsync(
        ProcessGroupTextInput input,
        CancellationToken cancellationToken
    );
}
