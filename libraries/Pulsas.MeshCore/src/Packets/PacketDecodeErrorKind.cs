namespace Pulsas.MeshCore.Packets;

public enum PacketDecodeErrorKind
{
    InvalidFrameLength,
    TruncatedFraming,
    InvalidPathLength,
    InvalidPayloadLength,
}
