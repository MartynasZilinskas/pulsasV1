using NodaTime;

namespace Pulsas.Persistence.Paths;

// A distinct signed flood advert received by an observer. Endpoints derived from
// prefixes are deliberately not frozen: a newly discovered collision invalidates them.
public class StoredNeighbourObservation
{
    public required Guid Id { get; set; }
    public Guid SourcePacketId { get; set; }
    public Guid SourceReceptionId { get; set; }
    public Guid ObserverId { get; set; }
    public Guid AdvertiserNodeId { get; set; }
    public byte[]? FirstHopPrefix { get; set; }
    public Instant? FirstHeardAt { get; set; }
    public Instant? LastHeardAt { get; set; }
}
