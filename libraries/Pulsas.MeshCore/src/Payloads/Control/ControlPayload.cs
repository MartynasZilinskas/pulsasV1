using System.Collections.Immutable;

namespace Pulsas.MeshCore.Payloads.Control;

// A recognized subtype does not imply that its body has been interpreted or validated.
public record ControlPayload(ControlSubtype Subtype, ImmutableArray<byte> Body)
{
    public byte SubtypeCode => (byte)Subtype;
}
