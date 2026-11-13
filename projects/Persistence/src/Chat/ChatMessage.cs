using NodaTime;

namespace Pulsas.Persistence.Chat;

public class ChatMessage
{
    public required Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public required byte[] PayloadHash { get; set; }
    public string? ClaimedSenderName { get; set; }
    public required string Text { get; set; }
    public Instant SentAt { get; set; }
}
