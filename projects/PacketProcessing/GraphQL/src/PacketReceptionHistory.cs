using System.Collections.Immutable;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;
using static Pulsas.PacketProcessing.GraphQL.ReceptionQuery;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class PacketReceptionHistory
{
    internal static async Task<MeshPacketReceptionConnection> ReadAsync(
        Guid parent,
        bool forPacket,
        int? first,
        string? after,
        ReceptionsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    )
    {
        var collection = forPacket
            ? "meshPacketReceptionsByPacket"
            : "meshPacketReceptionsByPayload";
        var scope = parent.ToString("D");
        var size = PageSize(first);
        var rows = await pages.LoadRequiredAsync(
            new ReceptionPage(
                parent,
                forPacket,
                size + 1,
                PositionOf(after, collection, scope, cursors)
            ),
            cancellationToken
        );
        var edges = rows.Take(size)
            .Select(row => new MeshPacketReceptionEdge(
                cursors.Encode(new(collection, scope, row.Id, Time: Format(row.ArrivedAt))),
                new(row)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }
}
