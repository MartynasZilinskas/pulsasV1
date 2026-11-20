using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    [Cost(12_001)]
    [GraphQLDescription(
        "Observed overlap of distinct exact wire packets with Flood or TransportFlood routing in the [from, to) server-arrival window, up to 30 days. Nonoverlap does not measure packet loss."
    )]
    public async Task<IObserverFloodOverlapResult?> GetObserverFloodOverlapAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] string observerAId,
        [GraphQLType(typeof(NonNullType<IdType>))] string observerBId,
        Instant from,
        Instant to,
        ObserverActivityFilterInput? filter,
        [Service] IMeshTrafficReadManager reads,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(observerAId, out var a) || !Guid.TryParse(observerBId, out var b))
        {
            throw Error("INVALID_INPUT", "Expected two observer UUIDs.");
        }
        if (a == b)
        {
            throw Error("INVALID_INPUT", "The observer UUIDs must be distinct.");
        }

        var window = TrafficWindowFrom(
            from,
            to,
            new(null, filter?.RegionCodes, filter?.PacketTypes)
        );
        ObserverFloodOverlapInput input = new(
            a,
            b,
            from,
            to,
            window.RegionCodes,
            window.PacketTypes
        );
        var overlap = await reads.ReadObserverFloodOverlapAsync(input, cancellationToken);
        return overlap.Match<IObserverFloodOverlapResult>(
            left: missing => new OverlapObserversNotFoundError(missing.ObserverIds),
            right: counts => new ObserverFloodOverlap(input, counts)
        );
    }
}
