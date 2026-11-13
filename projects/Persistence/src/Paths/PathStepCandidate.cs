namespace Pulsas.Persistence.Paths;

// Catalogue membership is shared; confidence and evidence remain packet-contextual.
public class PathStepCandidate
{
    public Guid PathStepId { get; set; }
    public Guid MeshNodeId { get; set; }
}
