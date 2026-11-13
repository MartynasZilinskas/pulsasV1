using NodaTime;
using Pulsas.Persistence.Regions;

namespace Pulsas.Persistence.Nodes;

public class MeshNodeRegionHearing
{
    public Guid MeshNodeId { get; set; }
    public MeshNode MeshNode { get; set; } = null!;
    public required string RegionCode { get; set; }
    public StoredRegion Region { get; set; } = null!;
    public Instant? LastHeardAt { get; set; }
}
