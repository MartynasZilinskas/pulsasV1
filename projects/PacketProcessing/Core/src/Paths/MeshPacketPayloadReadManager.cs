using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using NpgsqlTypes;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.Persistence;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshPacketPayloadReadManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public class MeshPacketPayloadReadManager(IDbContextFactory<TrackingDbContext> dbFactory)
    : IMeshPacketPayloadReadManager
{
    private const string PacketColumns = """
        p."Id" AS "PacketId", p."ContentId", p."ShortHash", p."PacketHash", p."RawPacket",
        p."SignatureStatus"::text AS "SignatureStatus",
        CASE WHEN cl."Id" IS NOT NULL THEN m."Id" END AS "PublicChatMessageId",
        stats.n AS "PacketReceptionCount", stats.first_at AS "PacketFirstArrivedAt",
        stats.last_at AS "PacketLastArrivedAt"
        """;

    private const string PacketChatJoins = """
        LEFT JOIN chat_messages m ON m."Id" = p."ChatMessageId"
        LEFT JOIN channels cl ON cl."Id" = m."ChannelId" AND cl."Public" AND cl."Active"
        """;

    private const string PacketStatsJoin = """
        CROSS JOIN LATERAL (
            SELECT count(*) AS n, min(r."ArrivedAt") AS first_at, max(r."ArrivedAt") AS last_at
            FROM packet_receptions r WHERE r."PacketId" = p."Id"
        ) stats
        """;

    public Task<ImmutableArray<PayloadRow>> ReadPayloadsAsync(
        int limit,
        Position? after,
        CancellationToken cancellationToken
    ) => ReadPayloadsCoreAsync(null, limit, after, cancellationToken);

    public Task<ImmutableArray<PayloadRow>> ReadPayloadsByIdAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken
    ) => ReadPayloadsCoreAsync(ids, ids.Count, null, cancellationToken);

    private async Task<ImmutableArray<PayloadRow>> ReadPayloadsCoreAsync(
        IReadOnlyList<Guid>? ids,
        int limit,
        Position? after,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Seek on per-payload minima; enrich only the page. The representative, its
        // metadata and eligibility all come from the same PostgreSQL statement snapshot.
        var rows = await db
            .Database.SqlQueryRaw<PayloadProjection>(
                $$"""
                WITH page AS MATERIALIZED (
                    SELECT p."ContentId" AS "Id", min(first."ArrivedAt") AS "FirstArrivedAt"
                    FROM packets p
                    JOIN LATERAL (
                        SELECT r."ArrivedAt" FROM packet_receptions r WHERE r."PacketId" = p."Id"
                        ORDER BY r."ArrivedAt" LIMIT 1
                    ) first ON true
                    WHERE (NOT @by_id OR p."ContentId" = ANY(@ids))
                    GROUP BY p."ContentId"
                    HAVING (NOT @seek OR (min(first."ArrivedAt"), p."ContentId") < (@time, @after))
                    ORDER BY "FirstArrivedAt" DESC, p."ContentId" DESC
                    LIMIT @limit
                )
                SELECT c."Id", c."Type", c."PayloadVersion", c."Payload", stats."PacketCount",
                       stats."ReceptionCount", page."FirstArrivedAt", stats."LastArrivedAt",
                       {{PacketColumns}}
                FROM page JOIN packet_contents c ON c."Id" = page."Id"
                CROSS JOIN LATERAL (
                    SELECT f."Id", counts.*, count(*) OVER () AS "PacketCount",
                           sum(counts.n) OVER ()::bigint AS "ReceptionCount",
                           max(counts.last_at) OVER () AS "LastArrivedAt"
                    FROM packets f CROSS JOIN LATERAL (
                        SELECT count(*) AS n, min(r."ArrivedAt") AS first_at, max(r."ArrivedAt") AS last_at
                        FROM packet_receptions r WHERE r."PacketId" = f."Id"
                    ) counts WHERE f."ContentId" = page."Id"
                    ORDER BY counts.first_at NULLS LAST, f."Id" LIMIT 1
                ) stats
                JOIN packets p ON p."Id" = stats."Id"
                {{PacketChatJoins}}
                ORDER BY page."FirstArrivedAt" DESC, page."Id" DESC
                """,
                new NpgsqlParameter("by_id", ids is not null),
                new NpgsqlParameter("ids", ids?.ToArray() ?? []),
                new NpgsqlParameter("seek", after is not null),
                new NpgsqlParameter<Instant?>("time", NpgsqlDbType.TimestampTz)
                {
                    TypedValue = after?.Time,
                },
                new NpgsqlParameter<Guid?>("after", NpgsqlDbType.Uuid) { TypedValue = after?.Id },
                new NpgsqlParameter("limit", limit)
            )
            .ToArrayAsync(cancellationToken);
        return
        [
            .. rows.Select(row => new PayloadRow(
                row.Id,
                row.Type,
                row.PayloadVersion,
                row.Payload,
                row.PacketCount,
                row.ReceptionCount,
                row.FirstArrivedAt,
                row.LastArrivedAt,
                row.ToPacket()
            )),
        ];
    }

    public async Task<ImmutableArray<PacketPageRow>> ReadPacketsAsync(
        IReadOnlyList<PacketPage> pages,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db
            .Database.SqlQueryRaw<PacketPageProjection>(
                $$"""
                SELECT (requests.ordinality - 1)::int AS "PageIndex", {{PacketColumns}}
                FROM unnest(@ids, @limits, @afters) WITH ORDINALITY AS requests(id, page_limit, after_id, ordinality)
                CROSS JOIN LATERAL (
                    SELECT f.* FROM packets f
                    WHERE f."ContentId" = requests.id AND (requests.after_id IS NULL OR f."Id" > requests.after_id)
                    ORDER BY f."Id" LIMIT requests.page_limit
                ) p
                {{PacketChatJoins}}
                {{PacketStatsJoin}}
                ORDER BY requests.ordinality, p."Id"
                """,
                new NpgsqlParameter("ids", pages.Select(page => page.PayloadId).ToArray()),
                new NpgsqlParameter("limits", pages.Select(page => page.Limit).ToArray()),
                new NpgsqlParameter("afters", pages.Select(page => page.After).ToArray())
            )
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PacketPageRow(row.PageIndex, row.ToPacket()))];
    }

    public async Task<ImmutableArray<PacketRow>> ReadPacketsByIdAsync(
        IReadOnlyList<PacketKey> keys,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db
            .Database.SqlQueryRaw<PacketProjection>(
                $$"""
                SELECT {{PacketColumns}}
                FROM unnest(@payload_ids, @packet_ids) AS requests(payload_id, packet_id)
                JOIN packets p ON p."Id" = requests.packet_id AND p."ContentId" = requests.payload_id
                {{PacketChatJoins}}
                {{PacketStatsJoin}}
                """,
                new NpgsqlParameter("payload_ids", keys.Select(key => key.PayloadId).ToArray()),
                new NpgsqlParameter("packet_ids", keys.Select(key => key.PacketId).ToArray())
            )
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => row.ToPacket())];
    }

    public async Task<ImmutableArray<ReceptionPageRow>> ReadHistoryAsync(
        IReadOnlyList<ReceptionPage> pages,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var keys = db.Database.SqlQuery<ReceptionPageKey>(
            $"""
            SELECT (requests.ordinality - 1)::int AS "PageIndex", history."Id", history."ArrivedAt"
            FROM unnest({pages.Select(page => page.ParentId).ToArray()},
                        {pages.Select(page => page.ForPacket).ToArray()},
                        {pages.Select(page => page.Limit).ToArray()},
                        {pages.Select(page => page.After?.Time).ToArray()},
                        {pages.Select(page => page.After?.Id).ToArray()}) WITH ORDINALITY
                AS requests(id, for_packet, page_limit, after_at, after_id, ordinality)
            CROSS JOIN LATERAL (
                SELECT matches."Id", matches."ArrivedAt" FROM (
                    SELECT r."Id", r."ArrivedAt" FROM packet_receptions r
                    WHERE requests.for_packet AND r."PacketId" = requests.id
                    UNION ALL
                    SELECT r."Id", r."ArrivedAt" FROM packets p
                    JOIN packet_receptions r ON r."PacketId" = p."Id"
                    WHERE NOT requests.for_packet AND p."ContentId" = requests.id
                ) matches
                WHERE requests.after_at IS NULL OR (matches."ArrivedAt", matches."Id") < (requests.after_at, requests.after_id)
                ORDER BY matches."ArrivedAt" DESC, matches."Id" DESC LIMIT requests.page_limit
            ) history
            """
        );
        return
        [
            .. await keys.OrderBy(key => key.PageIndex)
                .ThenByDescending(key => key.ArrivedAt)
                .ThenByDescending(key => key.Id)
                .SelectMany(key =>
                    db.MeshPacketReceptions.Where(row => row.Id == key.Id)
                        .Select(ReceptionReadManager.Reception)
                        .Select(reception => new ReceptionPageRow(key.PageIndex, reception))
                )
                .ToArrayAsync(cancellationToken),
        ];
    }

    private record ReceptionPageKey(int PageIndex, Guid Id, Instant ArrivedAt);

    public async Task<ImmutableArray<PacketStatsRow>> ReadPacketStatsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var keys = ids.ToArray();
        return
        [
            .. await db
                .MeshPacketReceptions.Where(row =>
                    row.PacketId.HasValue && keys.Contains(row.PacketId.Value)
                )
                .GroupBy(row => row.PacketId!.Value)
                .Select(rows => new PacketStatsRow(
                    rows.Key,
                    rows.LongCount(),
                    rows.Min(row => row.ArrivedAt),
                    rows.Max(row => row.ArrivedAt)
                ))
                .ToArrayAsync(cancellationToken),
        ];
    }

    private class PacketProjection
    {
        public Guid PacketId { get; init; }
        public Guid ContentId { get; init; }
        public string ShortHash { get; init; } = null!;
        public byte[] PacketHash { get; init; } = null!;
        public byte[] RawPacket { get; init; } = null!;
        public string SignatureStatus { get; init; } = null!;
        public Guid? PublicChatMessageId { get; init; }
        public long PacketReceptionCount { get; init; }
        public Instant? PacketFirstArrivedAt { get; init; }
        public Instant? PacketLastArrivedAt { get; init; }

        internal PacketRow ToPacket() =>
            new(
                PacketId,
                ShortHash,
                PacketHash,
                RawPacket,
                SignatureStatus switch
                {
                    "not_checked" => PacketSignatureStatus.NotChecked,
                    "malformed" => PacketSignatureStatus.Malformed,
                    "invalid_signature" => PacketSignatureStatus.InvalidSignature,
                    "verified" => PacketSignatureStatus.Verified,
                    _ => throw new InvalidOperationException("Unexpected stored signature status."),
                },
                PublicChatMessageId,
                ContentId,
                new(PacketReceptionCount, PacketFirstArrivedAt, PacketLastArrivedAt)
            );
    }

    private class PacketPageProjection : PacketProjection
    {
        public int PageIndex { get; init; }
    }

    private class PayloadProjection : PacketProjection
    {
        public Guid Id { get; init; }
        public short Type { get; init; }
        public short PayloadVersion { get; init; }
        public byte[] Payload { get; init; } = null!;
        public long PacketCount { get; init; }
        public long ReceptionCount { get; init; }
        public Instant FirstArrivedAt { get; init; }
        public Instant LastArrivedAt { get; init; }
    }
}
