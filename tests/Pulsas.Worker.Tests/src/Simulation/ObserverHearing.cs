using System.Collections.Immutable;
using NodaTime;

namespace Pulsas.Worker.Tests.Simulation;

public record ObserverHearing(
    string ObserverId,
    ImmutableArray<byte> ObserverPublicKey,
    ImmutableArray<byte> Raw,
    Instant HeardAt
);
