using NodaTime;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public record ReceptionReadDetails(
    ObserverRow? Observer,
    BrokerRow? Broker,
    RegionRow? Region,
    PacketRow? Packet
);

public record ObserverRow(
    Guid Id,
    byte[] PublicKey,
    string? Name,
    Instant ModifiedAt,
    Instant? LastOnlineAt
);

public record BrokerRow(Guid Id, string Name);

public record RegionRow(Guid Id, string Code, string? Name, string CountryCode);

public record PacketRow(
    Guid Id,
    string ShortHash,
    byte[] PacketHash,
    byte[] Raw,
    PacketSignatureStatus SignatureStatus,
    Guid? PublicChatMessageId,
    Guid? ContentId = null,
    PacketReceptionStats? Stats = null
);

public record PacketReceptionStats(long Count, Instant? FirstArrivedAt, Instant? LastArrivedAt);

public enum PacketSignatureStatus
{
    NotChecked,
    Malformed,
    InvalidSignature,
    Verified,
}
