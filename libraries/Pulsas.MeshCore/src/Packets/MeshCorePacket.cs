using System.Collections.Immutable;

namespace Pulsas.MeshCore.Packets;

// Raw path bytes are deliberately neutral: TRACE uses them for SNR progress.
public record MeshCorePacket(
    byte Header,
    byte EncodedPathLength,
    ImmutableArray<byte> TransportCodes,
    ImmutableArray<byte> Path,
    ImmutableArray<byte> Payload
)
{
    public RouteType RouteType => (RouteType)(Header & 3);
    public PacketType PacketType => (PacketType)((Header >> 2) & 15);
    public int PayloadVersion => Header >> 6;
    public int PathWidth => (EncodedPathLength >> 6) + 1;
    public int PathCount => EncodedPathLength & 63;
    public bool HasTransportCodes =>
        RouteType is RouteType.TransportFlood or RouteType.TransportDirect;
    public bool IsSupportedVersion => PayloadVersion == 0;
    public bool IsSupportedPathWidth => PathWidth <= 3;
}
