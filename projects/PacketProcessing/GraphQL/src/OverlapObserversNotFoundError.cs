using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;

namespace Pulsas.PacketProcessing.GraphQL;

public record OverlapObserversNotFoundError(
    [property: GraphQLType(typeof(NonNullType<ListType<NonNullType<IdType>>>))]
    [property: ListSize(AssumedSize = 2)]
        ImmutableArray<Guid> MissingObserverIds
) : IObserverFloodOverlapResult
{
    public string Message => "One or both observers were not found.";
}
