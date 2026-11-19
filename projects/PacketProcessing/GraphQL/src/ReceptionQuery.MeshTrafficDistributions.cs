using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    [Cost(12_001)]
    [GraphQLDescription(
        "Frame-size and signal histograms for stored observations in the exact [from, to) server-arrival window, up to 30 days."
    )]
    public async Task<MeshTrafficDistributions?> GetMeshTrafficDistributionsAsync(
        Instant from,
        Instant to,
        MeshTrafficFilterInput? filter,
        [Service] IMeshTrafficReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var window = TrafficWindowFrom(from, to, filter);
        var result = await reads.ReadDistributionsAsync(window, cancellationToken);
        return new(from, to, result);
    }
}
