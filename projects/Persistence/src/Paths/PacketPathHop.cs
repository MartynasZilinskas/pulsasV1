namespace Pulsas.Persistence.Paths;

public class PacketPathHop
{
    public required Guid Id { get; set; }
    public Guid PacketId { get; set; }
    public short Position { get; set; }
    public Guid PathStepId { get; set; }
    public PathStep Step { get; set; } = null!;
}
