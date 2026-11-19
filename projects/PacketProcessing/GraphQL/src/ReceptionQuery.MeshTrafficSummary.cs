using HotChocolate.CostAnalysis.Types;
using HotChocolate.Resolvers;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    // At most two independent summary reads fit the Gateway's field budget.
    [Cost(12_001)]
    [GraphQLDescription(
        "Counts currently stored records in the exact [from, to) server-arrival window, up to 30 days. A zero count does not establish historical coverage."
    )]
    public async Task<MeshTrafficSummary?> GetMeshTrafficSummaryAsync(
        Instant from,
        Instant to,
        MeshTrafficFilterInput? filter,
        IResolverContext context,
        [Service] IMeshTrafficReadManager reads,
        CancellationToken cancellationToken
    )
    {
        if (from >= to || to - from > Duration.FromDays(30))
        {
            throw Error(
                "INVALID_INPUT",
                "The arrival range must be increasing and at most 30 days."
            );
        }

        var normalized = ReceptionBrowserFilter.FromInput(
            new(filter?.ObserverIds, filter?.RegionCodes, filter?.PacketTypes, null, null)
        );
        return new(
            from,
            to,
            await reads.ReadSummaryAsync(
                new(
                    from,
                    to,
                    normalized.Observers,
                    normalized.Regions,
                    normalized.Types,
                    context.IsSelected("packetTypeCounts")
                ),
                cancellationToken
            )
        );
    }
}
