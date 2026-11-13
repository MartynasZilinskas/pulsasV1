using Pulsas.MeshCore.Packets;

namespace Pulsas.Persistence.Packets;

public class StoredPacketContent
{
    public required Guid Id { get; set; }
    public PacketType Type { get; set; }
    public short PayloadVersion { get; set; }
    public required byte[] PayloadHash { get; set; }
    public required byte[] Payload { get; set; }
    public ICollection<PacketContentTag> Tags { get; set; } = [];
}
