using System.Collections.Immutable;
using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class ObserverActivitySummary(
    Instant from,
    Instant to,
    int intervalSeconds,
    ObserverActivity activity
) : IObserverActivitySummaryResult
{
    public Instant From => from;
    public Instant To => to;
    public int IntervalSeconds => intervalSeconds;
    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;
    public long ReceptionCount => activity.ReceptionCount;

    [GraphQLDescription("Earliest matching server arrival, or null when no reception matches.")]
    public Instant? FirstArrivedAt => activity.FirstArrivedAt.ToNullable();

    [GraphQLDescription("Latest matching server arrival, or null when no reception matches.")]
    public Instant? LastArrivedAt => activity.LastArrivedAt.ToNullable();

    public MeshTrafficSignalSummary Snr =>
        new(activity.Snr.SampleCount, activity.Snr.Average.ToNullable());
    public MeshTrafficSignalSummary Rssi =>
        new(activity.Rssi.SampleCount, activity.Rssi.Average.ToNullable());

    [ListSize(AssumedSize = 17)]
    [GraphQLDescription(
        "Matching receptions by packet type. A null type counts unlinked receptions."
    )]
    public ImmutableArray<MeshPacketReceptionTypeCount> PacketTypeCounts =>
        [
            .. activity.PacketTypeCounts.Select(row => new MeshPacketReceptionTypeCount(
                row.PacketType.Select(type => (LegacyPacketType)type).ToNullable(),
                row.Count
            )),
        ];

    [ListSize(AssumedSize = TrafficBuckets.MaximumBuckets)]
    public ImmutableArray<ObserverActivityBucket> Buckets
    {
        get
        {
            var counts = activity.Buckets.ToDictionary(row => row.Start, row => row.ReceptionCount);
            return
            [
                .. TrafficBuckets
                    .Enumerate(from, to, intervalSeconds)
                    .Select(bucket => new ObserverActivityBucket(
                        bucket.From,
                        bucket.To,
                        counts.GetValueOrDefault(bucket.Start)
                    )),
            ];
        }
    }
}
