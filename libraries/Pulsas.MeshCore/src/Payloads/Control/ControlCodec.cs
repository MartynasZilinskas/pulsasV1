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

    public static byte[] Encode(ControlPayload control)
    {
        if (control.Body.IsDefault || control.Body.Length > PacketCodec.MaximumPayloadLength - 1)
        {
            throw new ArgumentException("Invalid control wrapper body.", nameof(control));
        }

        return [control.SubtypeCode, .. control.Body];
    }
}
