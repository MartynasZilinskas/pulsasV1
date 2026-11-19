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
    [property: ListSize(AssumedSize = MeshTrafficTrend.MaximumBuckets)]
        ImmutableArray<MeshTrafficTrendBucket> Buckets
)
{
    internal const int MaximumBuckets = 120;
    private static readonly int[] Intervals =
    [
        60,
        300,
        900,
        1800,
        3600,
        7200,
        14400,
        28800,
        43200,
        86400,
    ];

    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;

    internal static int SelectInterval(Instant from, Instant to) =>
        Intervals.First(seconds =>
            BinIndex(to - Duration.Epsilon, seconds) - BinIndex(from, seconds) + 1 <= MaximumBuckets
        );

    internal static ImmutableArray<MeshTrafficTrendBucket> BuildBuckets(
        Instant from,
        Instant to,
        int seconds,
        ImmutableArray<TrendBucket> rows
    )
    {
        var indexed = rows.ToDictionary(row => row.Start);
        var buckets = ImmutableArray.CreateBuilder<MeshTrafficTrendBucket>();
        var width = Duration.FromSeconds(seconds);
        var start = Instant.FromUnixTimeSeconds(BinIndex(from, seconds) * seconds);
        while (start < to)
        {
            var end = to - start <= width ? to : start + width;
            var row = indexed.GetValueOrDefault(start);
            buckets.Add(
                new(
                    start < from ? from : start,
                    end,
                    row?.ReceptionCount ?? 0,
                    row?.WirePacketCount ?? 0,
                    row?.SharedPayloadCount ?? 0,
                    Signal(row?.Snr),
                    Signal(row?.Rssi)
                )
            );
            start = end;
        }
        return buckets.ToImmutable();
    }

    // Integer division rounds toward zero; bins must floor before the Unix epoch too.
    private static long BinIndex(Instant time, int seconds)
    {
        var unix = time.ToUnixTimeSeconds();
        return unix / seconds - (unix % seconds < 0 ? 1 : 0);
    }

    private static MeshTrafficSignalSummary Signal(SignalSummary? row) =>
        row is null ? new(0, null) : new(row.SampleCount, row.Average.ToNullable());
}
