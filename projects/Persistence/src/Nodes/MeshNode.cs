using NetTopologySuite.Geometries;
using NodaTime;
using Pulsas.MeshCore.Nodes;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Regions;

namespace Pulsas.Persistence.Nodes;

public class MeshNode
{
    public Instant? CreatedAt { get; set; }
    public Instant? ModifiedAt { get; set; }
    public MeshNodeSourceType SourceType { get; set; }
    public required Guid Id { get; set; }
    public required byte[] PublicKey { get; set; }
    public Guid? LatestAdvertPacketId { get; set; }
    public MeshPacket? LatestAdvertPacket { get; set; }
    public Instant? LatestAdvertHeardAt { get; set; }

    // Signed Unix seconds of the last accepted metadata, not a radio-hearing time.
    public long? AdvertMetadataTimestamp { get; set; }
    public Guid? LocationRegionId { get; set; }
    public StoredRegion? LocationRegion { get; set; }
    public ICollection<MeshNodeRegionHearing> HeardInRegions { get; set; } = [];
    public string? Name { get; set; }
    public NodeRole? Role { get; set; }
    public Point? Location { get; set; }
}
