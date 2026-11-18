using System.Collections.Immutable;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public record PathCandidateAssessment(
    Guid NodeId,
    string PublicKey,
    PathCandidateConfidence Confidence,
    bool Selected,
    ImmutableArray<PathCandidateReason> Reasons,
    ImmutableArray<PathNeighbourSupport> Supports
);
