namespace Pulsas.MeshCore.Packets;

public enum PacketDecodeErrorKind
{
    Unknown = 0,
    InvalidPacketLength = 1,
    TruncatedPacket = 2,
    InvalidPathLength = 3,
    InvalidPayloadLength = 4,
}
