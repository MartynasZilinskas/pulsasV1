using HotChocolate.CostAnalysis.Types;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

[ObjectType<MeshPacketPayload>]
internal static partial class MeshPacketPayloadNode
{
    [Cost(MeshPacketPayload.ReadCost)]
    [GraphQLDescription(
        "Looks up an exact wire packet by UUID within this payload. Returns null if the packet is missing or belongs to another payload."
    )]
    public static Task<MeshPacket?> GetPacketAsync(
        [Parent(requires: nameof(MeshPacketPayload.Id))] MeshPacketPayload payload,
        [GraphQLType(typeof(NonNullType<IdType>))] string id,
        PayloadPacketByIdDataLoader packets,
        CancellationToken cancellationToken
    ) =>
        Guid.TryParse(id, out var packetId)
            ? packets.LoadAsync(new PacketKey(payload.Id, packetId), cancellationToken)
            : throw ReceptionBrowserCursor.Error("INVALID_INPUT", "Expected a mesh packet UUID.");

    [Cost(MeshPacketPayload.ReadCost)]
    [GraphQLDescription(
        "Wire packets in packet ID order. First defaults to 20 (range 1–100); after is scoped to this payload's packet list."
    )]
    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public static Task<MeshPacketConnection> GetPacketsAsync(
        [Parent(requires: nameof(MeshPacketPayload.Id))] MeshPacketPayload payload,
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        PacketsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    ) => payload.ReadPacketsAsync(first, after, pages, cursors, cancellationToken);

    [Cost(MeshPacketPayload.ReadCost)]
    [GraphQLDescription(
        "Receptions across this payload's packets, newest server arrival first. First defaults to 20 (range 1–100); after is scoped to this payload's reception history."
    )]
    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public static Task<MeshPacketReceptionConnection> GetReceptionsAsync(
        [Parent(requires: nameof(MeshPacketPayload.Id))] MeshPacketPayload payload,
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        ReceptionsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    ) => payload.ReadReceptionsAsync(first, after, pages, cursors, cancellationToken);
}
