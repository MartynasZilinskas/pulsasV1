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

    public static Either<MultipartEncodeError, byte[]> Encode(MultipartPayload multipart)
    {
        if (multipart.Remaining > 15)
        {
            return Either<MultipartEncodeError, byte[]>.Left(
                new(MultipartEncodeErrorKind.InvalidRemaining)
            );
        }

        if ((byte)multipart.PayloadType > 15)
        {
            return Either<MultipartEncodeError, byte[]>.Left(
                new(MultipartEncodeErrorKind.InvalidPayloadType)
            );
        }

        if (multipart.Body.IsDefault)
        {
            return Either<MultipartEncodeError, byte[]>.Left(
                new(MultipartEncodeErrorKind.UninitializedBody)
            );
        }

        if (multipart.Body.Length > PacketCodec.MaximumPayloadLength - 1)
        {
            return Either<MultipartEncodeError, byte[]>.Left(
                new(MultipartEncodeErrorKind.InvalidBodyLength, multipart.Body.Length)
            );
        }

        return Either<MultipartEncodeError, byte[]>.Right([
            (byte)((multipart.Remaining << 4) | (byte)multipart.PayloadType),
            .. multipart.Body,
        ]);
    }
}
