using System.Diagnostics.CodeAnalysis;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.Multipart;

public static class MultipartCodec
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out MultipartPayload? multipart
    )
    {
        multipart = null;
        if (payload.Length is < 1 or > PacketCodec.MaximumPayloadLength)
        {
            return false;
        }

        multipart = new((byte)(payload[0] >> 4), (PacketType)(payload[0] & 15), [.. payload[1..]]);
        return true;
    }

    public static MultipartPayload Decode(ReadOnlySpan<byte> payload) =>
        TryDecode(payload, out var multipart)
            ? multipart
            : throw new FormatException("Malformed multipart wrapper.");

    public static byte[] Encode(MultipartPayload multipart)
    {
        if (
            multipart.Remaining > 15
            || (byte)multipart.PayloadType > 15
            || multipart.Body.IsDefault
            || multipart.Body.Length > PacketCodec.MaximumPayloadLength - 1
        )
        {
            throw new ArgumentException("Invalid multipart wrapper fields.", nameof(multipart));
        }

        return
        [
            (byte)((multipart.Remaining << 4) | (byte)multipart.PayloadType),
            .. multipart.Body,
        ];
    }
}
