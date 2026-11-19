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

    public record TrafficWindow(
        Instant From,
        Instant To,
        ImmutableArray<Guid> ObserverIds,
        ImmutableArray<string> RegionCodes,
        ImmutableArray<PacketType> PacketTypes
    );

    public record SummaryInput(TrafficWindow Window, bool IncludePacketTypeCounts);

    public record TrendInput(TrafficWindow Window, Duration BucketWidth);

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
