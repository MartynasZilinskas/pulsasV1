using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;
using Pulsas.MeshCore.Packets;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

[UnionType(Name = "PacketResult")]
public interface IPacketResult;

public record PacketDecodeError(string Message) : IPacketResult;

public record MeshPacket(
    [property: GraphQLType(typeof(NonNullType<IdType>))] Guid Id,
    string ShortHash,
    string PacketHash,
    RouteType RouteType,
    PathPrefixWidth PathPrefixWidth,
    [property: ListSize(AssumedSize = PacketCodec.MaximumPathLength)]
        IReadOnlyList<PacketPathHop>? PathHops,
    IPacketPayload? Payload,
    [property: GraphQLIgnore] Guid? ContentId,
    [property: GraphQLIgnore] PacketReceptionStats? Stats
) : IPacketResult
{
    internal static IPacketResult From(PacketRow? row)
    {
        return row is not null && PacketCodec.TryDecode(row.Raw, out var packet)
            ? Project(row, packet)
            : new PacketDecodeError("No decoded packet is available for this reception.");
    }

    internal static MeshPacket FromStored(PacketRow row) =>
        PacketCodec.TryDecode(row.Raw, out var packet)
            ? Project(row, packet)
            : throw new InvalidOperationException("A stored wire packet could not be decoded.");

    private static MeshPacket Project(PacketRow row, MeshCorePacket packet) =>
        new(
            row.Id,
            row.ShortHash,
            Convert.ToHexStringLower(row.PacketHash),
            packet.RouteType,
            (PathPrefixWidth)packet.PathWidth,
            Hops(packet),
            PacketPayloadProjection.Read(packet, row),
            row.ContentId,
            row.Stats
        );

    internal static IReadOnlyList<PacketPathHop>? Hops(MeshCorePacket packet)
    {
        if (
            packet.PacketType == PacketType.Trace
            || !packet.IsSupportedPathWidth
            || !packet.IsSupportedVersion
        )
        {
            return null;
        }

        return Enumerable
            .Range(0, packet.PathCount)
            .Select(position => new PacketPathHop(
                Convert.ToHexStringLower(
                    packet.Path.AsSpan(position * packet.PathWidth, packet.PathWidth)
                )
            ))
            .ToImmutableArray();
    }
}

public record PacketPathHop(string Prefix);

public enum PathPrefixWidth
{
    OneByte = 1,
    TwoBytes,
    ThreeBytes,
    Reserved,
}
