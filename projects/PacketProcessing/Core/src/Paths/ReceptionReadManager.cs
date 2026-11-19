using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Text.Json;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.Persistence;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionBrowserManager;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public class ReceptionReadManager(IDbContextFactory<TrackingDbContext> dbFactory)
    : IReceptionReadManager,
        IReceptionBrowserManager
{
    private const int MaximumAssessmentBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions AssessmentReadOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    internal static readonly Expression<Func<MeshPacketReception, ReadReceptionResult>> Reception =
        row =>
            new(
                row.Id,
                row.ArrivedAt,
                row.HeardAt,
                row.Signal.Rssi,
                row.Signal.Snr,
                row.PacketId,
                new ReceptionReadDetails(
                    new ObserverRow(
                        row.ObserverId,
                        row.Observer.PublicKey,
                        row.Observer.Name,
                        row.Observer.ModifiedAt,
                        row.Observer.LastOnlineAt
                    ),
                    new BrokerRow(row.BrokerId, BrokerLabel(row.Broker.Endpoint)),
                    new RegionRow(
                        row.Region.Id,
                        row.RegionCode,
                        row.Region.Name,
                        row.Region.CountryCode
                    ),
                    row.Packet == null
                        ? null
                        : new PacketRow(
                            row.Packet.Id,
                            row.Packet.ShortHash,
                            row.Packet.PacketHash,
                            row.Packet.RawPacket,
                            ReadSignature(row.Packet.SignatureVerification.Status),
                            row.Packet.ChatMessage != null
                            && row.Packet.ChatMessage.Channel.Public
                            && row.Packet.ChatMessage.Channel.Active
                                ? row.Packet.ChatMessageId
                                : null,
                            row.Packet.ContentId
                        )
                )
            );

    private static PacketSignatureStatus ReadSignature(AdvertSignatureStatus value) =>
        value switch
        {
            AdvertSignatureStatus.NotChecked => PacketSignatureStatus.NotChecked,
            AdvertSignatureStatus.Malformed => PacketSignatureStatus.Malformed,
            AdvertSignatureStatus.InvalidSignature => PacketSignatureStatus.InvalidSignature,
            AdvertSignatureStatus.Verified => PacketSignatureStatus.Verified,
            _ => throw new InvalidOperationException("Unexpected stored signature status."),
        };

    private static string BrokerLabel(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Authority
            : "Broker";

    public async Task<Option<ReadReceptionResult>> ReadReceptionAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return Option.FromNullable(
            await db
                .MeshPacketReceptions.Where(row => row.Id == id)
                .Select(Reception)
                .SingleOrDefaultAsync(cancellationToken)
        );
    }

    public async Task<ImmutableArray<ReadReceptionResult>> ReadReceptionsAsync(
        ReceptionFilter filter,
        int limit,
        Position? after,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.MeshPacketReceptions.AsNoTracking();
        if (!filter.ObserverIds.IsEmpty)
        {
            var ids = filter.ObserverIds.ToArray();
            query = query.Where(row => ids.Contains(row.ObserverId));
        }
        if (!filter.RegionCodes.IsEmpty)
        {
            var regions = filter.RegionCodes.ToArray();
            query = query.Where(row => regions.Contains(row.RegionCode));
        }
        if (!filter.PacketTypes.IsEmpty)
        {
            var types = filter.PacketTypes.ToArray();
            query = query.Where(row => row.Packet != null && types.Contains(row.Packet.Type));
        }
        if (filter.ArrivedFrom is { } from)
        {
            var bound = StoredBoundary(from);
            query = query.Where(row => row.ArrivedAt >= bound);
        }
        if (filter.ArrivedTo is { } to)
        {
            var bound = StoredBoundary(to);
            query = query.Where(row => row.ArrivedAt < bound);
        }
        if (after is not null)
        {
            query = query.Where(row =>
                EF.Functions.LessThan(
                    ValueTuple.Create(row.ArrivedAt, row.Id),
                    ValueTuple.Create(after.Time, after.Id)
                )
            );
        }
        return
        [
            .. await query
                .OrderByDescending(row => row.ArrivedAt)
                .ThenByDescending(row => row.Id)
                .Take(limit)
                .Select(Reception)
                .ToArrayAsync(cancellationToken),
        ];
    }

    public async Task<ImmutableArray<ObserverRow>> ReadObserversAsync(
        string? name,
        Instant? onlineAfter,
        bool? online,
        int limit,
        Position? after,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Observers.AsNoTracking();
        if (name is not null)
        {
            var term = name.ToLowerInvariant();
            query = query.Where(row => row.Name != null && row.Name.ToLower().Contains(term));
        }
        if (online is { } isOnline && onlineAfter is { } cutoff)
        {
            query = query.Where(row =>
                (row.LastOnlineAt != null && row.LastOnlineAt > cutoff) == isOnline
            );
        }
        if (after is not null)
        {
            query = query.Where(row =>
                EF.Functions.LessThan(
                    ValueTuple.Create(row.ModifiedAt, row.Id),
                    ValueTuple.Create(after.Time, after.Id)
                )
            );
        }
        return
        [
            .. await query
                .OrderByDescending(row => row.ModifiedAt)
                .ThenByDescending(row => row.Id)
                .Take(limit)
                .Select(row => new ObserverRow(
                    row.Id,
                    row.PublicKey,
                    row.Name,
                    row.ModifiedAt,
                    row.LastOnlineAt
                ))
                .ToArrayAsync(cancellationToken),
        ];
    }

    public async Task<ImmutableArray<RegionRow>> ReadRegionsAsync(
        int limit,
        string? afterCode,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Regions.AsNoTracking();
        if (afterCode is not null)
        {
            query = query.Where(row => string.Compare(row.Code, afterCode) > 0);
        }
        return
        [
            .. await query
                .OrderBy(row => row.Code)
                .Take(limit)
                .Select(row => new RegionRow(row.Id, row.Code, row.Name, row.CountryCode))
                .ToArrayAsync(cancellationToken),
        ];
    }

    public async Task<string?> ReadRegionCodeAsync(
        Guid regionId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Regions.Where(row => row.Id == regionId)
            .Select(row => row.Code)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ImmutableArray<HistogramCount>> ReadHistogramAsync(
        ReceptionFilter filter,
        Instant from,
        Instant to,
        int intervalSeconds,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = filter.ObserverIds.ToArray();
        var regions = filter.RegionCodes.ToArray();
        var types = filter.PacketTypes.Select(type => (short)type).ToArray();
        return
        [
            .. await db
                .Database.SqlQuery<HistogramCount>(
                    $"""
                    SELECT date_bin(make_interval(secs => {intervalSeconds}), r."ArrivedAt",
                                    TIMESTAMPTZ '1970-01-01 00:00:00+00') AS "Bucket",
                           p."Type" AS "Type", count(*) AS "Count"
                    FROM packet_receptions r LEFT JOIN packets p ON p."Id" = r."PacketId"
                    WHERE r."ArrivedAt" >= {StoredBoundary(
                        from
                    )} AND r."ArrivedAt" < {StoredBoundary(to)}
                      AND ({ids.Length == 0} OR r."ObserverId" = ANY({ids}))
                      AND ({regions.Length == 0} OR r."RegionCode" = ANY({regions}))
                      AND ({types.Length == 0} OR p."Type" = ANY({types}))
                    GROUP BY 1, 2
                    """
                )
                .ToArrayAsync(cancellationToken),
        ];
    }

    internal static Instant StoredBoundary(Instant instant)
    {
        const int ticksPerMicrosecond = 10;
        var ticks = instant.ToUnixTimeTicks();
        var floor = Instant.FromUnixTimeTicks(
            ticks - ((ticks % ticksPerMicrosecond + ticksPerMicrosecond) % ticksPerMicrosecond)
        );
        return floor == instant ? floor : floor + Duration.FromTicks(ticksPerMicrosecond);
    }

    public async Task<
        Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>
    > ReadPathAssessmentAsync(Guid receptionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Bound the JSONB transfer without treating an oversized row as missing.
        var stored = await db
            .Database.SqlQuery<StoredAssessment>(
                $"""
                SELECT "AssessedAt", "PolicyVersion",
                       CASE WHEN octet_length("Result"::text) <= {MaximumAssessmentBytes}
                            THEN "Result"::text END AS "Result"
                FROM reception_path_assessments
                WHERE "ReceptionId" = {receptionId}
                """
            )
            .SingleOrDefaultAsync(cancellationToken);
        return Option
            .FromNullable(stored)
            .Match(
                none: () =>
                    Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>.Right(
                        Option<ReadPathAssessmentResult>.None
                    ),
                some: ReadAssessment
            );
    }

    private static Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>> ReadAssessment(
        StoredAssessment stored
    )
    {
        if (stored.Result is not { } json)
        {
            return Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>.Left(
                ReadPathAssessmentError.SnapshotTooLarge
            );
        }

        var assessment = JsonSerializer.Deserialize<PathAssessmentResult>(
            json,
            AssessmentReadOptions
        ) ?? throw new InvalidOperationException("The stored assessment has no result.");
        return Either<ReadPathAssessmentError, Option<ReadPathAssessmentResult>>.Right(
            Option.Some(
                new ReadPathAssessmentResult(stored.AssessedAt, stored.PolicyVersion, assessment)
            )
        );
    }

    private record StoredAssessment(Instant AssessedAt, string PolicyVersion, string? Result);
}
