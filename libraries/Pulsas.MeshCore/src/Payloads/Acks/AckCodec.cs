using System.Diagnostics.CodeAnalysis;
using Funcky.Monads;

namespace Pulsas.MeshCore.Payloads.Acks;

public static class AckCodec
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out AckPayload? ack
    )
    {
        ack = Decode(payload).Match<AckPayload?>(left: _ => null, right: value => value);
        return ack is not null;
    }

    public static Either<AckError, AckPayload> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 4)
        {
            return Either<AckError, AckPayload>.Left(
                new(AckErrorKind.InvalidChecksumLength, payload.Length)
            );
        }

        return new AckPayload([.. payload]);
    }

    public static Either<AckError, byte[]> Encode(AckPayload ack)
    {
        if (ack.Checksum.IsDefault)
        {
            return Either<AckError, byte[]>.Left(new(AckErrorKind.UninitializedChecksum));
        }

        if (ack.Checksum.Length != 4)
        {
            return Either<AckError, byte[]>.Left(
                new(AckErrorKind.InvalidChecksumLength, ack.Checksum.Length)
            );
        }

        return Either<AckError, byte[]>.Right([.. ack.Checksum]);
    }
}
