using System.Collections.Immutable;
using NodaTime;

namespace Pulsas.Worker.Tests.Simulation;

public record NetworkTraceEntry(
    long TransmissionId,
    string TransmitterId,
    string ReceiverId,
    ImmutableArray<byte> Raw,
    Instant HeardAt,
    int? HearingIndex,
    string? ForwardingDrop
);
