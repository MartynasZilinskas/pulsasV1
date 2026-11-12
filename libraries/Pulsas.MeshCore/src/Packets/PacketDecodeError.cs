namespace Pulsas.MeshCore.Packets;

// Length is the measured frame, path, or payload length named by Kind.
// For truncated framing it is the available frame length; MinimumLength includes payload.
public record PacketDecodeError(PacketDecodeErrorKind Kind, int Length, int? MinimumLength = null);
