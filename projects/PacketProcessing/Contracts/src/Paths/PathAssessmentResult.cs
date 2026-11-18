using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public record PathAssessmentResult(
    PathAssessmentKind PathKind,
    PathAssessmentStatus Status,
    ImmutableArray<PathHopAssessment> Hops
);
