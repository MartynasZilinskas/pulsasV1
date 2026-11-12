namespace Pulsas.MeshCore.Packets;

public enum PacketDecodeErrorKind
{
    InvalidPacketLength,
    TruncatedPacket,
    InvalidPathLength,
    InvalidPayloadLength,
}
