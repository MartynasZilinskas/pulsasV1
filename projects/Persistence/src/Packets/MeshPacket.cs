using NodaTime;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.Persistence.Chat;
using Pulsas.Persistence.Scopes;

namespace Pulsas.Persistence.Packets;

public class MeshPacket
{
    public required Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public StoredPacketContent Content { get; set; } = null!;
    public Guid? LastPathStepId { get; set; }
    public required byte[] PacketHash { get; set; }
    public required string ShortHash { get; set; }
    public required byte[] PayloadHash { get; set; }
    public PacketType Type { get; set; }
    public RouteType RouteType { get; set; }
    public byte[]? TransportCodes { get; set; }
    public short PathPrefixWidth { get; set; }
    public required byte[] RawPacket { get; set; }
    public string? DecodedPayload { get; set; }
    public string? DecoderRevision { get; set; }
    public AdvertSignatureVerification SignatureVerification { get; set; } = new();
    public Guid? ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    public Guid? ScopeId { get; set; }
    public TransportScope? Scope { get; set; }
    public ScopeResolutionStatus ScopeResolutionStatus { get; set; } =
        ScopeResolutionStatus.NotAssessed;
}

public record AdvertSignatureVerification
{
    public AdvertSignatureStatus Status { get; init; } = AdvertSignatureStatus.NotChecked;
    public AdvertSignatureErrorKind? FailureReason { get; init; }
    public Instant? CheckedAt { get; init; }
}
