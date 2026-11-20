using System.Collections.Immutable;
using Funcky.Monads;
using NodaTime;
using Pulsas.MeshCore.Packets;

namespace Pulsas.PacketProcessing.Contracts.Paths;

public interface IMeshTrafficReadManager
{
    Task<Summary> ReadSummaryAsync(SummaryInput input, CancellationToken cancellationToken);
    Task<ImmutableArray<TrendBucket>> ReadTrendAsync(
        TrendInput input,
        CancellationToken cancellationToken
    );
    Task<Distributions> ReadDistributionsAsync(
        TrafficWindow window,
        CancellationToken cancellationToken
    );
    Task<Option<ObserverActivity>> ReadObserverActivityAsync(
        ObserverActivityInput input,
        CancellationToken cancellationToken
    );
    Task<Either<MissingOverlapObservers, FloodOverlapCounts>> ReadObserverFloodOverlapAsync(
        ObserverFloodOverlapInput input,
        CancellationToken cancellationToken
    );

    public record TrafficWindow(
        Instant From,
        Instant To,
        ImmutableArray<Guid> ObserverIds,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes
    );

    public record SummaryInput(TrafficWindow Window, bool IncludePacketTypeCounts);

    public record TrendInput(TrafficWindow Window, Duration BucketWidth);

    public record ObserverActivityInput(
        Guid ObserverId,
        Instant From,
        Instant To,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes,
        Duration BucketWidth
    );

    public record ObserverActivity(
        long ReceptionCount,
        Option<Instant> FirstArrivedAt,
        Option<Instant> LastArrivedAt,
        SignalSummary Snr,
        SignalSummary Rssi,
        ImmutableArray<PacketTypeCount> PacketTypeCounts,
        ImmutableArray<ReceptionBucket> Buckets
    );

    public record ReceptionBucket(Instant Start, long ReceptionCount);

    public record ObserverFloodOverlapInput(
        Guid ObserverAId,
        Guid ObserverBId,
        Instant From,
        Instant To,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes
    );

    public record MissingOverlapObservers(ImmutableArray<Guid> ObserverIds);

    public record FloodOverlapCounts(long OnlyACount, long OnlyBCount, long BothCount)
    {
        public long UnionCount => OnlyACount + OnlyBCount + BothCount;
    }

    public record TrendBucket(
        Instant Start,
        long ReceptionCount,
        long WirePacketCount,
        long SharedPayloadCount,
        SignalSummary Snr,
        SignalSummary Rssi
    );

    public record Summary(
        long ReceptionCount,
        long WirePacketCount,
        long SharedPayloadCount,
        long ActiveObserverCount,
        long ActiveRegionCount,
        SignalSummary Snr,
        SignalSummary Rssi,
        ImmutableArray<PacketTypeCount> PacketTypeCounts,
        ImmutableArray<TagCount> Tags
    );

    public record SignalSummary(long SampleCount, Option<double> Average);

    public record Distributions(Distribution FrameSizeBytes, Distribution Snr, Distribution Rssi);

    public record Distribution(long SampleCount, ImmutableArray<DistributionBucket> Buckets);

    public record DistributionBucket(
        Option<int> LowerInclusive,
        Option<int> UpperExclusive,
        long Count
    );

    public record PacketTypeCount(Option<PacketType> PacketType, long Count);

    public record TagCount(string Tag, long SharedPayloadCount, long ReceptionCount);
}
