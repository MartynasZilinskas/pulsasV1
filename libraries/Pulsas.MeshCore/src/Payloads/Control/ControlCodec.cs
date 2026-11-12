using System.Diagnostics.CodeAnalysis;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.Control;

public static class ControlCodec
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out ControlPayload? control
    )
    {
        control = null;
        if (payload.Length is < 1 or > PacketCodec.MaximumPayloadLength)
        {
            return false;
        }

        control = new((ControlSubtype)payload[0], [.. payload[1..]]);
        return true;
    }

    public static ControlPayload Decode(ReadOnlySpan<byte> payload) =>
        TryDecode(payload, out var control)
            ? control
            : throw new FormatException("Malformed control wrapper.");

    public static byte[] Encode(ControlPayload control)
    {
        if (control.Body.IsDefault || control.Body.Length > PacketCodec.MaximumPayloadLength - 1)
        {
            throw new ArgumentException("Invalid control wrapper body.", nameof(control));
        }

        return [control.SubtypeCode, .. control.Body];
    }
}
