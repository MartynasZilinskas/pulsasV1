using System.Collections.Immutable;
using NodaTime;
using Pulsas.MeshCore.Packets;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;
using static Pulsas.PacketProcessing.GraphQL.ReceptionQuery;

namespace Pulsas.PacketProcessing.GraphQL;

public class MeshPacketPayload(PayloadRow row) : IMeshPacketPayloadByIdResult
{
    // With the Gateway's 36,000 field budget this permits a detail and its two
    // independently paged children, but not four grouped database reads.
    internal const int ReadCost = 9_001;

    [GraphQLType(typeof(NonNullType<IdType>))]
    public Guid Id => row.Id;
    public LegacyPacketType PayloadType => (LegacyPacketType)row.Type;
    public int PayloadVersion => row.PayloadVersion;
    public long PacketCount => row.PacketCount;
    public long ReceptionCount => row.ReceptionCount;
    public Instant FirstArrivedAt => row.FirstArrivedAt;
    public Instant LastArrivedAt => row.LastArrivedAt;

    [GraphQLDescription(
        "Partial payload metadata decoded without packet context. Does not include decrypted message text, chat linkage, or signature status. Null for unsupported payload versions or types."
    )]
    public IDecodedPayload? DecodedPayload =>
        PacketPayloadProjection.ReadShared((PacketType)row.Type, row.PayloadVersion, row.Payload);

    [GraphQLDescription(
        "The exact wire packet from the earliest retained reception, by server arrival time. This is not an inference about the original transmission."
    )]
    public MeshPacket FirstPacket => MeshPacket.FromStored(row.FirstPacket);

    internal async Task<MeshPacketConnection> ReadPacketsAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        PacketsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    )
    {
        const string collection = "meshPacketsByPayload";
        var scope = Id.ToString("D");
        var size = PageSize(first);
        var rows = await pages.LoadRequiredAsync(
            new PacketPage(
                Id,
                size + 1,
                after is null ? null : cursors.Decode(after, collection, scope).Id
            ),
            cancellationToken
        );
        var edges = rows.Take(size)
            .Select(packet => new MeshPacketEdge(
                cursors.Encode(new(collection, scope, packet.Id)),
                MeshPacket.FromStored(packet)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }

    internal Task<MeshPacketReceptionConnection> ReadReceptionsAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        ReceptionsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    ) =>
        PacketReceptionHistory.ReadAsync(
            Id,
            false,
            first,
            after,
            pages,
            cursors,
            cancellationToken
        );
}
