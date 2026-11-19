using System.Buffers.Binary;
using NodaTime;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Acks;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.MeshCore.Payloads.Control;
using Pulsas.MeshCore.Payloads.Multipart;
using Pulsas.PacketProcessing.Contracts.Paths;
using WireControlSubtype = Pulsas.MeshCore.Payloads.Control.ControlSubtype;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class PacketPayloadProjection
{
    internal static IPacketPayload? Read(MeshCorePacket packet, PacketRow row) =>
        Decode(packet.PacketType, packet.IsSupportedVersion ? packet.Payload.AsSpan() : []) switch
        {
            DecodedAdvertPayload advert => new AdvertPayload(
                advert.Type,
                advert.PublicKey,
                advert.Name,
                advert.Role,
                advert.AdvertisedTimestamp,
                row.SignatureStatus
            ),
            DecodedGroupTextPayload text => new GroupTextPayload(
                text.Type,
                text.ChannelHash,
                row.PublicChatMessageId is { } id ? new(id) : null
            ),
            IPacketPayload payload => payload,
            _ => null,
        };

    internal static IDecodedPayload? ReadShared(
        PacketType type,
        short version,
        ReadOnlySpan<byte> bytes
    ) => version == MeshCorePacket.SupportedPayloadVersion ? Decode(type, bytes) : null;

    private static IDecodedPayload? Decode(PacketType packetType, ReadOnlySpan<byte> body)
    {
        var type = (int)packetType;
        var destination = body.Length > 0 ? Convert.ToHexStringLower(body[..1]) : null;
        var source = body.Length > 1 ? Convert.ToHexStringLower(body[1..2]) : null;
        return packetType switch
        {
            PacketType.Request => new RequestPayload(type, destination, source),
            PacketType.Response => new ResponsePayload(type, destination, source),
            PacketType.Text => new TextPayload(type, destination, source),
            PacketType.Ack => new AckPayload(type, AckChecksum(body)),
            PacketType.Advert => Advert(type, body),
            PacketType.GroupText => new DecodedGroupTextPayload(type, destination),
            PacketType.GroupData => new GroupDataPayload(type, destination),
            PacketType.AnonymousRequest => new AnonymousRequestPayload(
                type,
                destination,
                body.Length >= 33 ? Convert.ToHexStringLower(body[1..33]) : null
            ),
            PacketType.Path => new PathPayload(type, destination, source),
            PacketType.Trace => new TracePayload(
                type,
                body.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(body) : null,
                body.Length >= 9 ? body[8] : null
            ),
            PacketType.Multipart => Multipart(type, body),
            PacketType.Control => Control(type, body),
            (PacketType)12 => new Reserved12Payload(type),
            (PacketType)13 => new Reserved13Payload(type),
            (PacketType)14 => new Reserved14Payload(type),
            PacketType.RawCustom => new RawCustomPayload(type),
            _ => null,
        };
    }

    private static DecodedAdvertPayload Advert(int type, ReadOnlySpan<byte> bytes)
    {
        if (!AdvertCodec.TryDecode(bytes, out var advert))
        {
            return new(
                type,
                bytes.Length >= 32 ? Convert.ToHexStringLower(bytes[..32]) : null,
                null,
                null,
                null
            );
        }

        AdvertAppDataCodec.TryDecode(advert.AppData.AsSpan(), out var metadata);
        return new(
            type,
            Convert.ToHexStringLower(advert.PublicKey.AsSpan()),
            metadata?.Name,
            metadata is null ? null : (LegacyNodeRole)metadata.RoleCode,
            Instant.FromUnixTimeSeconds(advert.Timestamp)
        );
    }

    private static MultipartPayload Multipart(int type, ReadOnlySpan<byte> bytes) =>
        MultipartCodec.TryDecode(bytes, out var multipart)
            ? new(
                type,
                multipart.Remaining,
                (int)multipart.PayloadType,
                multipart.PayloadType == PacketType.Ack
                    ? AckChecksum(multipart.Body.AsSpan())
                    : null
            )
            : new(type, null, null, null);

    private static ControlPayload Control(int type, ReadOnlySpan<byte> bytes) =>
        ControlCodec.TryDecode(bytes, out var control)
            ? new(
                type,
                control.Subtype switch
                {
                    WireControlSubtype.DiscoveryRequest => ControlSubtype.DiscoveryRequest,
                    WireControlSubtype.DiscoveryResponse => ControlSubtype.DiscoveryResponse,
                    _ => ControlSubtype.Unknown,
                },
                control.SubtypeCode
            )
            : new(type, null, null);

    private static string? AckChecksum(ReadOnlySpan<byte> bytes) =>
        AckCodec.TryDecode(bytes, out var ack)
            ? Convert.ToHexStringLower(ack.Checksum.AsSpan())
            : null;
}
