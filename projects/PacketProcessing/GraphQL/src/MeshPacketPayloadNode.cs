using HotChocolate.CostAnalysis.Types;

namespace Pulsas.PacketProcessing.GraphQL;

[ObjectType<MeshPacketPayload>]
internal static partial class MeshPacketPayloadNode
{
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
