using System.Collections.Immutable;
using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class MeshTrafficDistributions(Instant from, Instant to, Distributions result)
{
    public Instant From => from;
    public Instant To => to;
    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;

    [GraphQLDescription(
        "Stored frame lengths in bytes, one sample per distinct linked wire packet."
    )]
    public MeshTrafficDistribution FrameSizeBytes => Convert(result.FrameSizeBytes);

    [GraphQLDescription(
        "Finite SNR values in dB, one sample per matching reception with a valid reading."
    )]
    public MeshTrafficDistribution Snr => Convert(result.Snr);

    [GraphQLDescription(
        "RSSI values in dBm, one sample per matching reception with a stored reading."
    )]
    public MeshTrafficDistribution Rssi => Convert(result.Rssi);

    private static MeshTrafficDistribution Convert(Distribution distribution) =>
        new(
            distribution.SampleCount,
            [
                .. distribution.Buckets.Select(bucket => new MeshTrafficDistributionBucket(
                    bucket.LowerInclusive.ToNullable(),
                    bucket.UpperExclusive.ToNullable(),
                    bucket.Count
                )),
            ]
        );
}

public record MeshTrafficDistribution(
    long SampleCount,
    [property: ListSize(AssumedSize = 12)] ImmutableArray<MeshTrafficDistributionBucket> Buckets
);

public record MeshTrafficDistributionBucket(int? LowerInclusive, int? UpperExclusive, long Count);
