using Funcky;
using Funcky.Monads;
using Pulsas.Persistence.Nodes;
using static Pulsas.Persistence.Nodes.IAdvertNodeManager;

namespace Pulsas.Worker.Tests;

// Test-only recorder; database projection is exercised by AdvertIntegrationTests.
public class CaptureAdvertNodeManager : IAdvertNodeManager
{
    public List<ProcessAdvertInput> Inputs { get; } = [];

    public Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
        ProcessAdvertInput input,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Inputs.Add(input);
        return Task.FromResult(Either<ProcessAdvertError, Unit>.Right(default));
    }
}
