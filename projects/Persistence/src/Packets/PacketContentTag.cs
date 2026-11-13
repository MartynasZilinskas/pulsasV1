namespace Pulsas.Persistence.Packets;

public class PacketContentTag
{
    public Guid ContentId { get; set; }
    public required string Tag { get; set; }
}
