using System.Diagnostics.CodeAnalysis;
using Funcky.Monads;

namespace Pulsas.MeshCore.Packets;

// Handles packet framing; payload interpretation belongs to the payload codecs.
public static class PacketCodec
{
    public const int MaximumFrameLength = 255;
    public const int MaximumPathLength = 64;
    public const int MaximumPayloadLength = 184;

    public static bool TryDecode(
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out MeshCorePacket? packet
    )
    {
        packet = Decode(bytes).Match<MeshCorePacket?>(left: _ => null, right: value => value);
        return packet is not null;
    }

    public static Either<PacketDecodeError, MeshCorePacket> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 3 or > MaximumFrameLength)
        {
            return Either<PacketDecodeError, MeshCorePacket>.Left(
                new(PacketDecodeErrorKind.InvalidFrameLength, bytes.Length)
            );
        }

        var offset = (bytes[0] & 3) is 0 or 3 ? 5 : 1;
        if (bytes.Length <= offset)
        {
            return Either<PacketDecodeError, MeshCorePacket>.Left(
                new(PacketDecodeErrorKind.TruncatedFraming, bytes.Length, offset + 2)
            );
        }

        var encodedLength = bytes[offset++];
        var pathLength = (encodedLength & 63) * ((encodedLength >> 6) + 1);
        var payloadLength = bytes.Length - offset - pathLength;
        if (pathLength > MaximumPathLength)
        {
            return Either<PacketDecodeError, MeshCorePacket>.Left(
                new(PacketDecodeErrorKind.InvalidPathLength, pathLength)
            );
        }

        if (payloadLength is < 1 or > MaximumPayloadLength)
        {
            return Either<PacketDecodeError, MeshCorePacket>.Left(
                new(PacketDecodeErrorKind.InvalidPayloadLength, payloadLength)
            );
        }

        return new MeshCorePacket(
            bytes[0],
            encodedLength,
            [.. bytes[1..(offset - 1)]],
            [.. bytes.Slice(offset, pathLength)],
            [.. bytes[(offset + pathLength)..]]
        );
    }

    public static byte[] Encode(MeshCorePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (
            packet.TransportCodes.IsDefault
            || packet.Path.IsDefault
            || packet.Payload.IsDefault
            || packet.TransportCodes.Length != (packet.HasTransportCodes ? 4 : 0)
            || packet.Path.Length != packet.PathCount * packet.PathWidth
            || packet.Path.Length > MaximumPathLength
            || packet.Payload.Length is < 1 or > MaximumPayloadLength
        )
        {
            throw new ArgumentException("Inconsistent MeshCore packet fields.", nameof(packet));
        }

        byte[] bytes =
        [
            packet.Header,
            .. packet.TransportCodes,
            packet.EncodedPathLength,
            .. packet.Path,
            .. packet.Payload,
        ];
        if (bytes.Length > MaximumFrameLength)
        {
            throw new ArgumentException("MeshCore frame exceeds 255 bytes.", nameof(packet));
        }

        return bytes;
    }

    // Construction supports current ordinary path encoding. Decode/Encode also preserve
    // structurally valid future headers and width-four data without endorsing their use.
    public static MeshCorePacket Create(
        RouteType routeType,
        PacketType packetType,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> path = default,
        int pathWidth = 1,
        ReadOnlySpan<byte> transportCodes = default
    )
    {
        if ((byte)routeType > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(routeType));
        }

        if ((byte)packetType > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(packetType));
        }

        if (pathWidth is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(pathWidth));
        }

        if (path.Length % pathWidth != 0 || path.Length / pathWidth > 63)
        {
            throw new ArgumentException(
                "Path must contain complete prefixes, at most 63 entries.",
                nameof(path)
            );
        }

        var packet = new MeshCorePacket(
            (byte)(((byte)packetType << 2) | (byte)routeType),
            (byte)(((pathWidth - 1) << 6) | (path.Length / pathWidth)),
            [.. transportCodes],
            [.. path],
            [.. payload]
        );
        _ = Encode(packet);
        return packet;
    }
}
