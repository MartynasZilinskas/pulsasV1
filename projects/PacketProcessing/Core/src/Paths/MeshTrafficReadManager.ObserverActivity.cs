using System.Data;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.Persistence.Analytics;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public partial class MeshTrafficReadManager
{
    public async Task<Option<ObserverActivity>> ReadObserverActivityAsync(
        ObserverActivityInput input,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );
        var receptions = FilterReceptions(
            db.MeshPacketReceptions,
            new(input.From, input.To, [input.ObserverId], input.RegionCodes, input.PacketTypes)
        );
        var totals = await receptions
            .Select(row => new
            {
                row.ArrivedAt,
                Snr = row.Signal.Snr > double.NegativeInfinity
                && row.Signal.Snr < double.PositiveInfinity
                    ? Convert.ToDecimal(row.Signal.Snr!.Value.ToString())
                    : (decimal?)null,
                row.Signal.Rssi,
            })
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Receptions = group.LongCount(),
                First = group.Min(row => row.ArrivedAt),
                Last = group.Max(row => row.ArrivedAt),
                SnrCount = group.LongCount(row => row.Snr != null),
                SnrAverage = group.Average(row => row.Snr)!.Value.ToString(),
                RssiCount = group.LongCount(row => row.Rssi != null),
                RssiAverage = group.Average(row => row.Rssi),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // A matching reception already proves the observer exists. Otherwise distinguish an
        // unknown identity from a known observer with no matches in this snapshot.
        if (
            totals is null
            && !await db.Observers.AnyAsync(row => row.Id == input.ObserverId, cancellationToken)
        )
        {
            return Option<ObserverActivity>.None;
        }

        var types = totals is null
            ? []
            : await ReadPacketTypeCountsAsync(receptions, cancellationToken);
        var buckets = totals is null
            ? []
            : await receptions
                .GroupBy(row =>
                    ArrivalBuckets.Bin(input.BucketWidth, row.ArrivedAt, NodaConstants.UnixEpoch)
                )
                .Select(group => new ReceptionBucket(group.Key, group.LongCount()))
                .ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Option.Some(
            new ObserverActivity(
                totals?.Receptions ?? 0,
                Option.FromNullable(totals?.First),
                Option.FromNullable(totals?.Last),
                ReadSnrMean(totals?.SnrCount ?? 0, totals?.SnrAverage),
                new(totals?.RssiCount ?? 0, Option.FromNullable(totals?.RssiAverage)),
                [
                    .. types.Select(row => new PacketTypeCount(
                        Option.FromNullable(row.Type),
                        row.Count
                    )),
                ],
                [.. buckets]
            )
        );
    }
}
