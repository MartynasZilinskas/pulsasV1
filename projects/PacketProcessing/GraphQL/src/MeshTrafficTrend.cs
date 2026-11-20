using System.Collections.Immutable;
using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public record MeshTrafficTrend(
    Instant From,
    Instant To,
    int IntervalSeconds,
    [property: ListSize(AssumedSize = TrafficBuckets.MaximumBuckets)]
        ImmutableArray<MeshTrafficTrendBucket> Buckets
)
{
    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;

    internal static ImmutableArray<MeshTrafficTrendBucket> BuildBuckets(
        Instant from,
        Instant to,
        int seconds,
        ImmutableArray<TrendBucket> rows
    )
    {
        var indexed = rows.ToDictionary(row => row.Start);
        var buckets = ImmutableArray.CreateBuilder<MeshTrafficTrendBucket>();
        foreach (var (start, lower, upper) in TrafficBuckets.Enumerate(from, to, seconds))
        {
            var row = indexed.GetValueOrDefault(start);
            buckets.Add(
                new(
                    lower,
                    upper,
                    row?.ReceptionCount ?? 0,
                    row?.WirePacketCount ?? 0,
                    row?.SharedPayloadCount ?? 0,
                    Signal(row?.Snr),
                    Signal(row?.Rssi)
                )
            );
        }
        return buckets.ToImmutable();
    }

    private static MeshTrafficSignalSummary Signal(SignalSummary? row) =>
        row is null ? new(0, null) : new(row.SampleCount, row.Average.ToNullable());
}
