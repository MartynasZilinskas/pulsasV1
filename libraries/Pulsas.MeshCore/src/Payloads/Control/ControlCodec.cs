using System.Diagnostics.CodeAnalysis;
using Funcky.Monads;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.Control;

public static class ControlCodec
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out ControlPayload? control
    )
    {
        control = Decode(payload).Match<ControlPayload?>(left: _ => null, right: value => value);
        return control is not null;
    }

    public static Either<ControlDecodeError, ControlPayload> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 1 or > PacketCodec.MaximumPayloadLength)
        {
            return Either<ControlDecodeError, ControlPayload>.Left(new(payload.Length));
        }

        return new ControlPayload((ControlSubtype)payload[0], [.. payload[1..]]);
    }

    public static Either<ControlEncodeError, byte[]> Encode(ControlPayload control)
    {
        if (control.Body.IsDefault)
        {
            return Either<ControlEncodeError, byte[]>.Left(
                new(ControlEncodeErrorKind.UninitializedBody)
            );
        }

        if (control.Body.Length > PacketCodec.MaximumPayloadLength - 1)
        {
            return Either<ControlEncodeError, byte[]>.Left(
                new(ControlEncodeErrorKind.InvalidBodyLength, control.Body.Length)
            );
        }

        return Either<ControlEncodeError, byte[]>.Right([control.SubtypeCode, .. control.Body]);
    }
}
