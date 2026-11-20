using NodaTime;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class ObserverFloodOverlap(ObserverFloodOverlapInput input, FloodOverlapCounts counts)
    : IObserverFloodOverlapResult
{
    [GraphQLType(typeof(NonNullType<IdType>))]
    public Guid ObserverAId => input.ObserverAId;

    [GraphQLType(typeof(NonNullType<IdType>))]
    public Guid ObserverBId => input.ObserverBId;

    public Instant From => input.From;
    public Instant To => input.To;
    public MeshTrafficHistoryCoverage HistoryCoverage => MeshTrafficHistoryCoverage.Unknown;
    public long OnlyACount => counts.OnlyACount;
    public long OnlyBCount => counts.OnlyBCount;
    public long BothCount => counts.BothCount;
    public long UnionCount => counts.UnionCount;
}
