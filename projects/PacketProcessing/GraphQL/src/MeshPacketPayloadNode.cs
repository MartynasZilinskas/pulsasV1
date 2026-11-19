using HotChocolate.CostAnalysis.Types;

namespace Pulsas.PacketProcessing.GraphQL;

[ObjectType<MeshPacketPayload>]
internal static partial class MeshPacketPayloadNode
{
    [Cost(MeshPacketPayload.ReadCost)]
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
