using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    [Cost(12_001)]
    [GraphQLDescription(
        "Observed activity for one observer in the exact [from, to) server-arrival window, up to 30 days. Empty counts do not establish downtime."
    )]
    public async Task<IObserverActivitySummaryResult?> GetObserverActivitySummaryAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] string observerId,
        Instant from,
        Instant to,
        ObserverActivityFilterInput? filter,
        [Service] IMeshTrafficReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var window = TrafficWindowFrom(
            from,
            to,
            new([observerId], filter?.RegionCodes, filter?.PacketTypes)
        );
        var interval = TrafficBuckets.SelectInterval(from, to);
        var activity = await reads.ReadObserverActivityAsync(
            new(
                window.ObserverIds[0],
                from,
                to,
                window.RegionCodes,
                window.PacketTypes,
                Duration.FromSeconds(interval)
            ),
            cancellationToken
        );
        return activity.Match<IObserverActivitySummaryResult>(
            none: () => new ObserverNotFoundError(),
            some: row => new ObserverActivitySummary(from, to, interval, row)
        );
    }
}
