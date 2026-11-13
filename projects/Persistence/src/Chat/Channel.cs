using Pulsas.PacketProcessing.Contracts.Chat;

namespace Pulsas.Persistence.Chat;

public class Channel
{
    public required Guid Id { get; set; }
    public ChannelKind Kind { get; set; }
    public string? ConfigurationId { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; }
    public bool Public { get; set; }
}
