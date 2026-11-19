using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

[InterfaceType(Name = "PacketPayload")]
public interface IPacketPayload
{
    int Type { get; }
}

[InterfaceType(Name = "DecodedPayload")]
public interface IDecodedPayload
{
    int Type { get; }
}

public record RequestPayload(int Type, string? DestinationHash, string? SourceHash)
    : IPacketPayload,
        IDecodedPayload;

public record ResponsePayload(int Type, string? DestinationHash, string? SourceHash)
    : IPacketPayload,
        IDecodedPayload;

public record TextPayload(int Type, string? DestinationHash, string? SourceHash)
    : IPacketPayload,
        IDecodedPayload;

public record AckPayload(int Type, string? ChecksumHex) : IPacketPayload, IDecodedPayload;

public record AdvertPayload(
    int Type,
    string? PublicKey,
    string? Name,
    LegacyNodeRole? Role,
    Instant? AdvertisedTimestamp,
    [property: GraphQLType(typeof(NonNullType<AdvertSignatureStatusType>))]
        PacketSignatureStatus SignatureStatus
) : IPacketPayload;

public record DecodedAdvertPayload(
    int Type,
    string? PublicKey,
    string? Name,
    LegacyNodeRole? Role,
    Instant? AdvertisedTimestamp
) : IDecodedPayload;

public class AdvertSignatureStatusType : EnumType<PacketSignatureStatus>
{
    protected override void Configure(IEnumTypeDescriptor<PacketSignatureStatus> descriptor) =>
        descriptor.Name("AdvertSignatureStatus");
}

public record GroupTextPayload(int Type, string? ChannelHash, ChatMessage? ChatMessage)
    : IPacketPayload;

public record DecodedGroupTextPayload(int Type, string? ChannelHash) : IDecodedPayload;

public record GroupDataPayload(int Type, string? ChannelHash) : IPacketPayload, IDecodedPayload;

public record AnonymousRequestPayload(int Type, string? DestinationHash, string? PublicKey)
    : IPacketPayload,
        IDecodedPayload;

public record PathPayload(int Type, string? DestinationHash, string? SourceHash)
    : IPacketPayload,
        IDecodedPayload;

public record TracePayload(int Type, long? Tag, int? Flags) : IPacketPayload, IDecodedPayload;

public record MultipartPayload(int Type, int? Remaining, int? PayloadType, string? ChecksumHex)
    : IPacketPayload,
        IDecodedPayload;

public record ControlPayload(int Type, ControlSubtype? Subtype, int? SubtypeCode)
    : IPacketPayload,
        IDecodedPayload;

[GraphQLName("Reserved12Payload")]
public record Reserved12Payload(int Type) : IPacketPayload, IDecodedPayload;

[GraphQLName("Reserved13Payload")]
public record Reserved13Payload(int Type) : IPacketPayload, IDecodedPayload;

[GraphQLName("Reserved14Payload")]
public record Reserved14Payload(int Type) : IPacketPayload, IDecodedPayload;

public record RawCustomPayload(int Type) : IPacketPayload, IDecodedPayload;

public record ChatMessage([property: GraphQLType(typeof(NonNullType<IdType>))] Guid Id);

public enum ControlSubtype
{
    DiscoveryRequest,
    DiscoveryResponse,
    Unknown,
}
