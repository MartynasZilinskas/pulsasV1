using System.Collections.Immutable;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class MeshPacketPayloadLoaders
{
    [DataLoader]
    public static async Task<Dictionary<Guid, MeshPacketPayload>> GetPayloadByIdAsync(
        IReadOnlyList<Guid> ids,
        IMeshPacketPayloadReadManager reads,
        CancellationToken cancellationToken
    ) =>
        (await reads.ReadPayloadsByIdAsync(ids, cancellationToken)).ToDictionary(
            row => row.Id,
            row => new MeshPacketPayload(row)
        );

    [DataLoader]
    public static async Task<Dictionary<Guid, MeshPacket>> GetPacketByIdAsync(
        IReadOnlyList<Guid> ids,
        IMeshPacketPayloadReadManager reads,
        CancellationToken cancellationToken
    ) =>
        (await reads.ReadPacketsByIdAsync(ids, cancellationToken)).ToDictionary(
            row => row.Id,
            MeshPacket.FromStored
        );

    [DataLoader]
    public static async Task<Dictionary<Guid, PacketReceptionStats>> GetPacketStatsByIdAsync(
        IReadOnlyList<Guid> ids,
        IMeshPacketPayloadReadManager reads,
        CancellationToken cancellationToken
    ) =>
        (await reads.ReadPacketStatsAsync(ids, cancellationToken)).ToDictionary(
            row => row.Id,
            row => new PacketReceptionStats(row.Count, row.FirstArrivedAt, row.LastArrivedAt)
        );

    // The keys are validated, signed-cursor positions, not GraphQL strings. Each SQL
    // batch slices per key, including an independent seek and limit for each parent.
    [DataLoader]
    public static async Task<
        Dictionary<PacketPage, ImmutableArray<PacketRow>>
    > GetPacketsByPageAsync(
        IReadOnlyList<PacketPage> pages,
        IMeshPacketPayloadReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var rows = (await reads.ReadPacketsAsync(pages, cancellationToken)).ToLookup(row =>
            row.PageIndex
        );
        return pages
            .Select((page, index) => (page, index))
            .ToDictionary(
                item => item.page,
                item => rows[item.index].Select(row => row.Packet).ToImmutableArray()
            );
    }

    [DataLoader]
    public static async Task<
        Dictionary<ReceptionPage, ImmutableArray<ReadReceptionResult>>
    > GetReceptionsByPageAsync(
        IReadOnlyList<ReceptionPage> pages,
        IMeshPacketPayloadReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var rows = (await reads.ReadHistoryAsync(pages, cancellationToken)).ToLookup(row =>
            row.PageIndex
        );
        return pages
            .Select((page, index) => (page, index))
            .ToDictionary(
                item => item.page,
                item => rows[item.index].Select(row => row.Reception).ToImmutableArray()
            );
    }
}
