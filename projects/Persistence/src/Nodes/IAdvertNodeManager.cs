using Funcky;
using Funcky.Monads;
using Pulsas.MeshCore.Payloads.Adverts;

namespace Pulsas.Persistence.Nodes;

public interface IAdvertNodeManager
{
    public record ProcessAdvertInput(AdvertPayload Advert, DateTimeOffset ArrivedAt);

    public enum ProcessAdvertError
    {
        InvalidSignature,
        ConcurrentNodeCreation,
    }

    Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
        ProcessAdvertInput input,
        CancellationToken cancellationToken
    );
}
