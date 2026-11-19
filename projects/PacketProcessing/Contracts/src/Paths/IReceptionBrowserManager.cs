using System.Collections.Immutable;
using NodaTime;
using Pulsas.MeshCore.Packets;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IReceptionBrowserManager
{
    Task<ImmutableArray<ReadReceptionResult>> ReadReceptionsAsync(
        ReceptionFilter filter,
        int limit,
        Position? after,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<ObserverRow>> ReadObserversAsync(
        string? name,
        Instant? onlineAfter,
        bool? online,
        int limit,
        Position? after,
        CancellationToken cancellationToken
    );
    Task<ImmutableArray<RegionRow>> ReadRegionsAsync(
        int limit,
        string? afterCode,
        CancellationToken cancellationToken
    );
    Task<string?> ReadRegionCodeAsync(Guid regionId, CancellationToken cancellationToken);
    Task<ImmutableArray<HistogramCount>> ReadHistogramAsync(
        ReceptionFilter filter,
        Instant from,
        Instant to,
        int intervalSeconds,
        CancellationToken cancellationToken
    );

    public record Position(Instant Time, Guid Id);

    public record ReceptionFilter(
        ImmutableArray<Guid> ObserverIds,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes,
        Instant? ArrivedFrom,
        Instant? ArrivedTo
    );

    public record HistogramCount(Instant Bucket, short? Type, long Count);
}
