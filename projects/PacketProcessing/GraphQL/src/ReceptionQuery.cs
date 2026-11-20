using System.Collections.Immutable;
using System.Text.Json;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

[GraphQLName("Query")]
public partial class ReceptionQuery
{
    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public async Task<MeshPacketReceptionConnection> GetMeshPacketReceptionsAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        MeshPacketReceptionFilterInput? filter,
        [Service] IReceptionBrowserManager reads,
        [Service] ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    )
    {
        const string collection = "meshPacketReceptions";
        var size = PageSize(first);
        var normalized = ReceptionBrowserFilter.FromInput(filter);
        var scope = Fingerprint(normalized.EncodeScope());
        var rows = await reads.ReadReceptionsAsync(
            normalized.ToFilter(),
            size + 1,
            PositionOf(after, collection, scope, cursors),
            cancellationToken
        );
        var edges = rows.Take(size)
            .Select(row => new MeshPacketReceptionEdge(
                cursors.Encode(new(collection, scope, row.Id, Time: Format(row.ArrivedAt))),
                new(row)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }

    [Cost(10)]
    public Task<MeshPacketReceptionHistogram> GetMeshPacketReceptionHistogramAsync(
        MeshPacketReceptionFilterInput? filter,
        [GraphQLType(typeof(CursorType))] string? refreshCursor,
        [Service] IReceptionBrowserManager reads,
        [Service] ReceptionBrowserCursor cursors,
        [Service] IClock clock,
        CancellationToken cancellationToken
    ) =>
        ReceptionHistogram.ReadAsync(
            filter,
            refreshCursor,
            clock.GetCurrentInstant(),
            cursors,
            reads,
            cancellationToken
        );

    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public async Task<ObserverConnection> GetObserversAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        ObserverFilterInput? filter,
        [Service] IReceptionBrowserManager reads,
        [Service] ReceptionBrowserCursor cursors,
        [Service] IClock clock,
        CancellationToken cancellationToken
    )
    {
        const string collection = "observers";
        var size = PageSize(first);
        if (filter?.Name?.Length > 200)
        {
            throw Error("INVALID_INPUT", "Observer name filter is too long.");
        }
        var scope = Fingerprint(JsonSerializer.Serialize(filter ?? new(null, null)));
        var now = clock.GetCurrentInstant();
        var rows = await reads.ReadObserversAsync(
            filter?.Name,
            Observer.Cutoff(now),
            filter?.Online,
            size + 1,
            PositionOf(after, collection, scope, cursors),
            cancellationToken
        );
        var edges = rows.Take(size)
            .Select(row => new ObserverEdge(
                cursors.Encode(new(collection, scope, row.Id, Time: Format(row.ModifiedAt))),
                Observer.From(row, now)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }

    [ListSize(
        AssumedSize = 100,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = 20,
        SizedFields = ["edges"],
        RequireOneSlicingArgument = false
    )]
    public async Task<RegionConnection> GetRegionsAsync(
        int? first,
        [GraphQLType(typeof(CursorType))] string? after,
        [Service] IReceptionBrowserManager reads,
        [Service] ReceptionBrowserCursor cursors,
        CancellationToken cancellationToken
    )
    {
        const string collection = "regions";
        var size = PageSize(first);
        string? afterCode = null;
        if (after is not null)
        {
            var cursor = cursors.Decode(after, collection);
            afterCode = cursor.RegionCode;
            if (afterCode is null)
            {
                afterCode = await reads.ReadRegionCodeAsync(cursor.Id, cancellationToken);
                if (afterCode is null || Fingerprint(afterCode) != cursor.Scope)
                {
                    throw InvalidCursor();
                }
            }
        }

        var rows = await reads.ReadRegionsAsync(size + 1, afterCode, cancellationToken);
        var edges = rows.Take(size)
            .Select(row => new RegionEdge(
                cursors.TryEncode(new(collection, "", row.Id, RegionCode: row.Code))
                    ?? cursors.Encode(new(collection, Fingerprint(row.Code), row.Id)),
                Region.From(row)
            ))
            .ToImmutableArray();
        return new(edges, Page(rows.Length, size, edges.Select(edge => edge.Cursor)));
    }

    [Cost(10)]
    [GraphQLDescription("Looks up a recorded reception by its UUID.")]
    public async Task<IMeshPacketReceptionByIdResult?> GetMeshPacketReceptionByIdAsync(
        [GraphQLType(typeof(NonNullType<IdType>))] string id,
        [Service] ReceptionByIdDataLoader receptions,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(id, out var receptionId))
        {
            throw Error("INVALID_INPUT", "Expected a reception UUID.");
        }

        var reception = await receptions.LoadAsync(receptionId, cancellationToken);
        return reception is null ? new MeshPacketReceptionNotFoundError() : reception;
    }

    internal static int PageSize(int? first) =>
        (first ?? 20) is >= 1 and <= 100 and var size
            ? size
            : throw Error("INVALID_INPUT", "Expected first between 1 and 100.");

    internal static Position? PositionOf(
        string? after,
        string collection,
        string scope,
        ReceptionBrowserCursor cursors
    )
    {
        if (after is null)
        {
            return null;
        }
        var cursor = cursors.Decode(after, collection, scope);
        return new(Parse(cursor.Time), cursor.Id);
    }

    internal static PageInfo Page(int returned, int size, IEnumerable<string> values)
    {
        var cursors = values.ToArray();
        return new(returned > size, false, cursors.FirstOrDefault(), cursors.LastOrDefault());
    }
}
