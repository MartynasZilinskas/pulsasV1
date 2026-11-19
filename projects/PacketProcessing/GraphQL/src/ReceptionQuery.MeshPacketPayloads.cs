using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    [Cost(MeshPacketPayload.ReadCost)]
    [GraphQLDescription(
        "Shared encoded payloads with retained receptions, newest first arrival first. First defaults to 20 (range 1–100); after is scoped to this feed."
    )]
    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public async Task<MeshPacketPayloadConnection> GetMeshPacketPayloadsAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        [Service] IMeshPacketPayloadReadManager reads,
        [Service] ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    )
    {
        const string collection = "meshPacketPayloads";
        const string scope = "";
        var size = PageSize(first);
        var rows = await reads.ReadPayloadsAsync(
            size + 1,
            PositionOf(after, collection, scope, cursors),
            cancellationToken
        );
        var edges = rows.Take(size)
            .Select(row => new MeshPacketPayloadEdge(
                cursors.Encode(new(collection, scope, row.Id, Time: Format(row.FirstArrivedAt))),
                new(row)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }

    [Cost(MeshPacketPayload.ReadCost)]
    public Task<MeshPacketPayload?> GetMeshPacketPayloadByIdAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] string id,
        [Service] PayloadByIdDataLoader payloads,
        CancellationToken cancellationToken
    ) =>
        Guid.TryParse(id, out var payloadId)
            ? payloads.LoadAsync(payloadId, cancellationToken)
            : throw Error("INVALID_INPUT", "Expected a mesh packet payload UUID.");
}
