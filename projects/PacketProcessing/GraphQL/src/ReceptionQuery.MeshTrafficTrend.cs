using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public partial class ReceptionQuery
{
    [Cost(12_001)]
    [GraphQLDescription(
        "Traffic counts and reception-level signal averages in automatically sized UTC buckets. Bounds are exact [from, to), up to 30 days."
    )]
    public async Task<MeshTrafficTrend?> GetMeshTrafficTrendAsync(
        Instant from,
        Instant to,
        MeshTrafficFilterInput? filter,
        [Service] IMeshTrafficReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var window = TrafficWindowFrom(from, to, filter);
        var intervalSeconds = MeshTrafficTrend.SelectInterval(from, to);
        var rows = await reads.ReadTrendAsync(
            new(window, Duration.FromSeconds(intervalSeconds)),
            cancellationToken
        );
        return new(
            from,
            to,
            intervalSeconds,
            MeshTrafficTrend.BuildBuckets(from, to, intervalSeconds, rows)
        );
    }
}
