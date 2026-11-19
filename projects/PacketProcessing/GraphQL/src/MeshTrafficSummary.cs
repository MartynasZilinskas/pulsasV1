using System.Collections.Immutable;
using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class MeshTrafficSummary(Instant from, Instant to, Summary summary)
{
    public Instant From => from;
    public Instant To => to;

    [GraphQLDescription("Whether historical data coverage is known, independent of stored counts.")]
    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;

    public long ReceptionCount => summary.ReceptionCount;
    public long WirePacketCount => summary.WirePacketCount;
    public long SharedPayloadCount => summary.SharedPayloadCount;

    public MeshTrafficSignalSummary Snr =>
        new(summary.Snr.SampleCount, summary.Snr.Average.ToNullable());
    public MeshTrafficSignalSummary Rssi =>
        new(summary.Rssi.SampleCount, summary.Rssi.Average.ToNullable());

    [ListSize(AssumedSize = 256)] // Cost estimate only; never truncate matching tags.
    [GraphQLDescription("Counts by current stored tag, sorted by tag. Counts can overlap.")]
    public ImmutableArray<MeshTrafficTagCount> Tags =>
        [
            .. summary.Tags.Select(row => new MeshTrafficTagCount(
                row.Tag,
                row.SharedPayloadCount,
                row.ReceptionCount
            )),
        ];
}

public enum MeshTrafficHistoryCoverage
{
    Unknown,
}

public record MeshTrafficSignalSummary(long SampleCount, double? Average);

public record MeshTrafficTagCount(string Tag, long SharedPayloadCount, long ReceptionCount);
