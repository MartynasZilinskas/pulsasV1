namespace Pulsas.Persistence.Paths;

public class StoredCandidateNeighbourSupport
{
    public required Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid NeighbourObservationId { get; set; }

    // Null anchor: advertiser boundary, or observer boundary with TargetReceptionId.
    public Guid? AnchorSegmentId { get; set; }
    public Guid? TargetReceptionId { get; set; }
}
