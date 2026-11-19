using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public record PageInfo(
    bool HasNextPage,
    bool HasPreviousPage,
    [property: GraphQLType(typeof(CursorType))] string? StartCursor,
    [property: GraphQLType(typeof(CursorType))] string? EndCursor
);

public record MeshPacketReceptionConnection(
    ImmutableArray<MeshPacketReceptionEdge> Edges,
    PageInfo PageInfo
);

public record MeshPacketReceptionEdge(
    [property: GraphQLType(typeof(NonNullType<CursorType>))] string Cursor,
    MeshPacketReception Node
);

public record ObserverConnection(ImmutableArray<ObserverEdge> Edges, PageInfo PageInfo);

public record ObserverEdge(
    [property: GraphQLType(typeof(NonNullType<CursorType>))] string Cursor,
    Observer Node
);

public record RegionConnection(ImmutableArray<RegionEdge> Edges, PageInfo PageInfo);

public record RegionEdge(
    [property: GraphQLType(typeof(NonNullType<CursorType>))] string Cursor,
    Region Node
);

public record Observer(
    [property: GraphQLType(typeof(NonNullType<IdType>))] Guid Id,
    string PublicKey,
    string? Name,
    Instant ModifiedAt,
    Instant? LastOnlineAt,
    bool Online
)
{
    internal static Observer From(ObserverRow row, Instant now) =>
        new(
            row.Id,
            Convert.ToHexStringLower(row.PublicKey),
            row.Name,
            row.ModifiedAt,
            row.LastOnlineAt,
            row.LastOnlineAt > Cutoff(now)
        );

    internal static Instant Cutoff(Instant now) => now - Duration.FromMinutes(1);
}

public record Broker([property: GraphQLType(typeof(NonNullType<IdType>))] Guid Id, string Name);

public record Region(
    [property: GraphQLType(typeof(NonNullType<IdType>))] Guid Id,
    string Code,
    string? Name,
    string CountryCode
)
{
    internal static Region From(RegionRow row) => new(row.Id, row.Code, row.Name, row.CountryCode);
}

public record MeshPacketReceptionHistogram(
    Instant From,
    Instant To,
    int IntervalSeconds,
    bool IsSnapshot,
    [property: GraphQLType(typeof(CursorType))] string? NextRefreshCursor,
    [property: ListSize(AssumedSize = 120)]
        ImmutableArray<MeshPacketReceptionHistogramBucket> Buckets
);

public record MeshPacketReceptionHistogramBucket(
    Instant From,
    Instant To,
    long Count,
    [property: ListSize(AssumedSize = 17)] ImmutableArray<MeshPacketReceptionTypeCount> PacketTypes
);

public record MeshPacketReceptionTypeCount(LegacyPacketType? PacketType, long Count);

[GraphQLName("PacketType")]
public enum LegacyPacketType
{
    Request,
    Response,
    Text,
    Ack,
    Advert,
    GroupText,
    GroupData,
    AnonymousRequest,
    Path,
    Trace,
    Multipart,
    Control,

    [GraphQLName("RESERVED12")]
    Reserved12,

    [GraphQLName("RESERVED13")]
    Reserved13,

    [GraphQLName("RESERVED14")]
    Reserved14,
    RawCustom,
}
