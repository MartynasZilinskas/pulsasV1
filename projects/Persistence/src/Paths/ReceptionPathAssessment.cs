using NodaTime;

namespace Pulsas.Persistence.Paths;

// Write once. Result includes identities and support endpoints, not references to mutable candidates.
public class ReceptionPathAssessment
{
    public Guid ReceptionId { get; init; }
    public Instant AssessedAt { get; init; }
    public required string PolicyVersion { get; init; }
    public bool AdmitsNeighbourEvidence { get; init; }
    public required string Result { get; init; }
}
