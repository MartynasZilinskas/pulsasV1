using System.Data;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.Persistence.Paths;

public class MeshTrafficReadManager(IDbContextFactory<TrackingDbContext> dbFactory)
    : IMeshTrafficReadManager
{
    public async Task<Summary> ReadSummaryAsync(
        SummaryInput input,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var from = ReceptionReadManager.StoredBoundary(input.From);
        var to = ReceptionReadManager.StoredBoundary(input.To);
        var receptions = db.MeshPacketReceptions.Where(row =>
            row.ArrivedAt >= from && row.ArrivedAt < to
        );
        if (!input.ObserverIds.IsEmpty)
        {
            var observers = input.ObserverIds.ToArray();
            receptions = receptions.Where(row => observers.Contains(row.ObserverId));
        }
        if (!input.RegionCodes.IsEmpty)
        {
            var regions = input.RegionCodes.ToArray();
            receptions = receptions.Where(row => regions.Contains(row.RegionCode));
        }
        if (!input.PacketTypes.IsEmpty)
        {
            var types = input.PacketTypes.ToArray();
            receptions = receptions.Where(row =>
                row.Packet != null && types.Contains(row.Packet.Type)
            );
        }

        // Totals and tags must see the same snapshot while ingestion continues.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );
        var totals = await receptions
            .Select(row => new
            {
                row.PacketId,
                ContentId = (Guid?)row.Packet!.ContentId,
                Snr = row.Signal.Snr > double.NegativeInfinity
                && row.Signal.Snr < double.PositiveInfinity
                    ? row.Signal.Snr
                    : null,
                row.Signal.Rssi,
            })
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Receptions = group.LongCount(),
                Packets = group
                    .Where(row => row.PacketId != null)
                    .Select(row => row.PacketId)
                    .Distinct()
                    .LongCount(),
                Payloads = group
                    .Where(row => row.ContentId != null)
                    .Select(row => row.ContentId)
                    .Distinct()
                    .LongCount(),
                SnrCount = group.LongCount(row => row.Snr != null),
                SnrAverage = group.Average(row => row.Snr),
                RssiCount = group.LongCount(row => row.Rssi != null),
                RssiAverage = group.Average(row => row.Rssi),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var contentCounts = receptions
            .Where(row => row.PacketId != null)
            .GroupBy(row => row.Packet!.ContentId)
            .Select(group => new { ContentId = group.Key, Receptions = group.LongCount() });
        TagCount[] tags =
            totals?.Payloads > 0
                ? await contentCounts
                    .Join(
                        db.PacketContentTags,
                        content => content.ContentId,
                        tag => tag.ContentId,
                        (content, tag) => new { tag.Tag, content.Receptions }
                    )
                    .GroupBy(row => row.Tag)
                    .OrderBy(group => group.Key)
                    .Select(group => new TagCount(
                        group.Key,
                        group.LongCount(),
                        group.Sum(row => row.Receptions)
                    ))
                    .ToArrayAsync(cancellationToken)
                : [];
        await transaction.CommitAsync(cancellationToken);
        return new(
            totals?.Receptions ?? 0,
            totals?.Packets ?? 0,
            totals?.Payloads ?? 0,
            new(totals?.SnrCount ?? 0, Option.FromNullable(totals?.SnrAverage)),
            new(totals?.RssiCount ?? 0, Option.FromNullable(totals?.RssiAverage)),
            [.. tags]
        );
    }
}
