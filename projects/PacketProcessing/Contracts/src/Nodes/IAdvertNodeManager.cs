using Funcky;
using Funcky.Monads;
using Pulsas.MeshCore.Payloads.Adverts;

namespace Pulsas.PacketProcessing.Contracts.Nodes;

public interface IAdvertNodeManager
{
    record ProcessAdvertInput(AdvertPayload Advert, DateTimeOffset ArrivedAt);

    enum ProcessAdvertError
    {
        InvalidSignature,
        ConcurrentNodeCreation,
    }

    Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
        ProcessAdvertInput input,
        CancellationToken cancellationToken
    );
}
