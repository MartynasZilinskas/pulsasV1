using NodaTime;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Regions;

namespace Pulsas.Persistence.Receptions;

public class MeshPacketReception
{
    public required Guid Id { get; set; }
    public Guid ObserverId { get; set; }
    public StoredObserver Observer { get; set; } = null!;
    public Guid BrokerId { get; set; }
    public StoredBroker Broker { get; set; } = null!;
    public Guid? PacketId { get; set; }
    public required string RegionCode { get; set; }
    public StoredRegion Region { get; set; } = null!;
    public Instant? HeardAt { get; set; }
    public Instant ArrivedAt { get; set; }
    public ReceptionSignal Signal { get; set; } = new();
    public byte[]? RawPacket { get; set; }
    public string? DecodeError { get; set; }
    public PacketAssociation Association { get; set; }
    public MeshPacket? Packet { get; set; }
}

public record ReceptionSignal
{
    public int? Rssi { get; init; }
    public double? Snr { get; init; }
}
