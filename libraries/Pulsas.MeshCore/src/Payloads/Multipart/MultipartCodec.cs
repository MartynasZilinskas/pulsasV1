using System.Diagnostics.CodeAnalysis;
using Funcky.Monads;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.Multipart;

public static class MultipartCodec
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out MultipartPayload? multipart
    )
    {
        multipart = Decode(payload)
            .Match<MultipartPayload?>(left: _ => null, right: value => value);
        return multipart is not null;
    }

    public static Either<MultipartDecodeError, MultipartPayload> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 1 or > PacketCodec.MaximumPayloadLength)
        {
            return Either<MultipartDecodeError, MultipartPayload>.Left(new(payload.Length));
        }

        return new MultipartPayload(
            (byte)(payload[0] >> 4),
            (PacketType)(payload[0] & 15),
            [.. payload[1..]]
        );
    }

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
