using Pulsas.Persistence.Nodes;

namespace Pulsas.Persistence.Paths;

public class PathHopCandidate
{
    public required Guid Id { get; set; }
    public Guid PathHopId { get; set; }
    public Guid PathStepId { get; set; }
    public Guid MeshNodeId { get; set; }
    public MeshNode MeshNode { get; set; } = null!;
    public PacketCandidateConfidence Confidence { get; set; }
    public required PacketCandidateReason[] Reasons { get; set; }
}
