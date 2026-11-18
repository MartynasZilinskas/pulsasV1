using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public record PathHopAssessment(
    int Position,
    int Width,
    string Prefix,
    PathHopOutcome Outcome,
    ImmutableArray<PathCandidateAssessment> Candidates
);
