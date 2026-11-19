using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public record ReceptionPathCandidate(
    [property: GraphQLType(typeof(NonNullType<IdType>))] Guid NodeId,
    string PublicKey,
    PathCandidateConfidence Confidence,
    bool Selected,
    [property: ListSize(AssumedSize = 3)] ImmutableArray<PathCandidateReason> Reasons,
    int SupportCount
)
{
    internal static ReceptionPathCandidate From(PathCandidateAssessment candidate) =>
        new(
            candidate.NodeId,
            candidate.PublicKey,
            candidate.Confidence,
            candidate.Selected,
            candidate.Reasons,
            candidate.Supports.Length
        );
}
