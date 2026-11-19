using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

[ObjectType<MeshPacket>]
internal static partial class MeshPacketNode
{
    // A default 20-packet detail page can select all stats, while a maximum
    // 100-row reception page cannot request multiple stats for every packet.
    private const int PacketStatsCost = 250;

    [Cost(MeshPacketPayload.ReadCost)]
    public static Task<MeshPacketPayload?> GetSharedPayloadAsync(
        [Parent(requires: nameof(MeshPacket.ContentId))] MeshPacket packet,
        PayloadByIdDataLoader payloads,
        CancellationToken cancellationToken
    ) =>
        packet.ContentId is { } id
            ? payloads.LoadAsync(id, cancellationToken)
            : Task.FromResult<MeshPacketPayload?>(null);

    [Cost(MeshPacketPayload.ReadCost)]
    [GraphQLDescription(
        "Receptions of this exact wire packet, newest server arrival first. First defaults to 20 (range 1–100); after is scoped to this packet's reception history."
    )]
    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public static Task<MeshPacketReceptionConnection> GetReceptionsAsync(
        [Parent(requires: nameof(MeshPacket.Id))] MeshPacket packet,
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        ReceptionsByPageDataLoader pages,
        ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    ) =>
        PacketReceptionHistory.ReadAsync(
            packet.Id,
            true,
            first,
            after,
            pages,
            cursors,
            cancellationToken
        );

    // All three scalar fields share one batched lookup on the legacy path and
    // use the already-materialized statistics on payload paths.
    [Cost(PacketStatsCost)]
    public static async Task<long> GetReceptionCountAsync(
        [Parent(requires: nameof(MeshPacket.Id) + "," + nameof(MeshPacket.Stats))]
            MeshPacket packet,
        PacketStatsByIdDataLoader stats,
        CancellationToken cancellationToken
    ) => (await StatsAsync(packet, stats, cancellationToken))?.Count ?? 0;

    [Cost(PacketStatsCost)]
    public static async Task<Instant?> GetFirstArrivedAtAsync(
        [Parent(requires: nameof(MeshPacket.Id) + "," + nameof(MeshPacket.Stats))]
            MeshPacket packet,
        PacketStatsByIdDataLoader stats,
        CancellationToken cancellationToken
    ) => (await StatsAsync(packet, stats, cancellationToken))?.FirstArrivedAt;

    [Cost(PacketStatsCost)]
    public static async Task<Instant?> GetLastArrivedAtAsync(
        [Parent(requires: nameof(MeshPacket.Id) + "," + nameof(MeshPacket.Stats))]
            MeshPacket packet,
        PacketStatsByIdDataLoader stats,
        CancellationToken cancellationToken
    ) => (await StatsAsync(packet, stats, cancellationToken))?.LastArrivedAt;

    private static async Task<PacketReceptionStats?> StatsAsync(
        MeshPacket packet,
        PacketStatsByIdDataLoader stats,
        CancellationToken cancellationToken
    ) => packet.Stats ?? await stats.LoadAsync(packet.Id, cancellationToken);
}
