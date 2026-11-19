using System.Collections.Immutable;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.Persistence.Analytics;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public partial class MeshTrafficReadManager
{
    public async Task<ImmutableArray<TrendBucket>> ReadTrendAsync(
        TrendInput input,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await FilterReceptions(db.MeshPacketReceptions, input.Window)
            .Select(row => new
            {
                Bucket = ArrivalBuckets.Bin(
                    input.BucketWidth,
                    row.ArrivedAt,
                    NodaConstants.UnixEpoch
                ),
                row.PacketId,
                ContentId = (Guid?)row.Packet!.ContentId,
                Snr = row.Signal.Snr > double.NegativeInfinity
                && row.Signal.Snr < double.PositiveInfinity
                    ? Convert.ToDecimal(row.Signal.Snr!.Value.ToString())
                    : (decimal?)null,
                row.Signal.Rssi,
            })
            .GroupBy(row => row.Bucket)
            .Select(group => new
            {
                Start = group.Key,
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
                SnrAverage = group.Average(row => row.Snr)!.Value.ToString(),
                RssiCount = group.LongCount(row => row.Rssi != null),
                RssiAverage = group.Average(row => row.Rssi),
            })
            .ToArrayAsync(cancellationToken);
        return
        [
            .. rows.Select(row => new TrendBucket(
                row.Start,
                row.Receptions,
                row.Packets,
                row.Payloads,
                ReadSnrMean(row.SnrCount, row.SnrAverage),
                new(row.RssiCount, Option.FromNullable(row.RssiAverage))
            )),
        ];
    }
}
