using System.Collections.Immutable;

namespace Pulsas.Worker.Tests.Simulation;

public record NetworkObserver(
    string Id,
    ImmutableArray<byte> PublicKey,
    string Name,
    ObserverConfiguration Configuration
);
