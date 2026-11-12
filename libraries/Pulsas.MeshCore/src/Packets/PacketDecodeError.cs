namespace Pulsas.MeshCore.Packets;

// Length is the measured packet, path, or payload length named by Kind.
// For a truncated packet it is the available packet length; MinimumLength includes payload.
public record PacketDecodeError(PacketDecodeErrorKind Kind, int Length, int? MinimumLength = null);
