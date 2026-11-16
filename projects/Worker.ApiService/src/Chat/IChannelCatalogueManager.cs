using Funcky;
using Funcky.Monads;

namespace Pulsas.Worker.ApiService.Chat;

public interface IChannelCatalogueManager
{
    enum ReconcileChannelsError
    {
        ConcurrentChannelCreation,
    }

    // Caller owns the transaction; apply the whole authoritative snapshot before consumers start.
    Task<Either<ReconcileChannelsError, Unit>> ReconcileChannelsAsync(
        CancellationToken cancellationToken
    );
}
