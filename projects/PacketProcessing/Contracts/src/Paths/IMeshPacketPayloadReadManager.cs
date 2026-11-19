using System.Collections.Immutable;
using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IMeshPacketPayloadReadManager
{
    Task<ImmutableArray<PayloadRow>> ReadPayloadsAsync(
        int limit,
        Position? after,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<PayloadRow>> ReadPayloadsByIdAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<PacketPageRow>> ReadPacketsAsync(
        IReadOnlyList<PacketPage> pages,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<PacketRow>> ReadPacketsByIdAsync(
        IReadOnlyList<PacketKey> keys,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<ReceptionPageRow>> ReadHistoryAsync(
        IReadOnlyList<ReceptionPage> pages,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<PacketStatsRow>> ReadPacketStatsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken
    );

    public record PayloadRow(
        Guid Id,
        short Type,
        short PayloadVersion,
        byte[] Payload,
        long PacketCount,
        long ReceptionCount,
        Instant FirstArrivedAt,
        Instant LastArrivedAt,
        PacketRow FirstPacket
    );

    public record PacketPage(Guid PayloadId, int Limit, Guid? After);

    public readonly record struct PacketKey(Guid PayloadId, Guid PacketId);

    public record PacketPageRow(int PageIndex, PacketRow Packet);

    public record ReceptionPage(Guid ParentId, bool ForPacket, int Limit, Position? After);

    public record ReceptionPageRow(int PageIndex, ReadReceptionResult Reception);

    public record PacketStatsRow(
        Guid Id,
        long Count,
        Instant? FirstArrivedAt,
        Instant? LastArrivedAt
    );
}
