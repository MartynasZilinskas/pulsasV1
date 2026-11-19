using System.Data;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Pulsas.Persistence;
using Pulsas.Persistence.Receptions;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public partial class MeshTrafficReadManager
{
    private const int FrameStep = 32;
    private const int FrameUpper = 256;
    private const int SnrLower = -30;
    private const int SnrUpper = 20;
    private const int SnrStep = 5;
    private const int RssiLower = -130;
    private const int RssiUpper = -30;
    private const int RssiStep = 10;

    public async Task<Distributions> ReadDistributionsAsync(
        TrafficWindow window,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var receptions = FilterReceptions(db.MeshPacketReceptions, window);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );
        var frameSizes = await ReadFrameSizesAsync(db, receptions, cancellationToken);
        var signals = await ReadSignalPairsAsync(receptions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(
            BuildDistribution(FrameStep, FrameUpper, FrameStep, frameSizes),
            BuildDistribution(SnrLower, SnrUpper, SnrStep, SignalCounts(signals, row => row.Snr)),
            BuildDistribution(
                RssiLower,
                RssiUpper,
                RssiStep,
                SignalCounts(signals, row => row.Rssi)
            )
        );
    }

    private static Task<Dictionary<int, long>> ReadFrameSizesAsync(
        TrackingDbContext db,
        IQueryable<MeshPacketReception> receptions,
        CancellationToken cancellationToken
    )
    {
        var packetIds = receptions
            .Where(row => row.PacketId != null)
            .Select(row => row.PacketId!.Value);
        return db
            .MeshPackets.Where(packet => packetIds.Contains(packet.Id))
            .GroupBy(packet =>
                packet.RawPacket.Length >= FrameUpper
                    ? FrameUpper / FrameStep
                    : packet.RawPacket.Length / FrameStep
            )
            .Select(group => new { Bin = group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(row => row.Bin, row => row.Count, cancellationToken);
    }

    private static Task<SignalPair[]> ReadSignalPairsAsync(
        IQueryable<MeshPacketReception> receptions,
        CancellationToken cancellationToken
    ) =>
        receptions
            .Select(row => new { row.Signal.Snr, row.Signal.Rssi })
            .GroupBy(row => new
            {
                Snr = row.Snr > double.NegativeInfinity && row.Snr < double.PositiveInfinity
                    ? row.Snr < SnrLower
                        ? 0
                        : row.Snr >= SnrUpper
                            ? (SnrUpper - SnrLower) / SnrStep + 1
                            // Floor before integer arithmetic: adding an offset to a double can round across a bin edge.
                            : ((int)Math.Floor(row.Snr!.Value) - SnrLower) / SnrStep + 1
                    : (int?)null,
                Rssi = row.Rssi == null ? (int?)null
                : row.Rssi < RssiLower ? 0
                : row.Rssi >= RssiUpper ? (RssiUpper - RssiLower) / RssiStep + 1
                : (row.Rssi.Value - RssiLower) / RssiStep + 1,
            })
            .Select(group => new SignalPair(group.Key.Snr, group.Key.Rssi, group.LongCount()))
            .ToArrayAsync(cancellationToken);

    private static Dictionary<int, long> SignalCounts(
        SignalPair[] signals,
        Func<SignalPair, int?> bin
    ) =>
        signals
            .Where(row => bin(row).HasValue)
            .GroupBy(row => bin(row)!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Count));

    private static Distribution BuildDistribution(
        int firstEdge,
        int lastEdge,
        int step,
        Dictionary<int, long> counts
    )
    {
        var lastBin = (lastEdge - firstEdge) / step + 1;
        return new(
            counts.Values.Sum(),
            [
                .. Enumerable
                    .Range(0, lastBin + 1)
                    .Select(bin => new DistributionBucket(
                        bin == 0 ? Option<int>.None : Option.Some(firstEdge + (bin - 1) * step),
                        bin == lastBin ? Option<int>.None : Option.Some(firstEdge + bin * step),
                        counts.GetValueOrDefault(bin)
                    )),
            ]
        );
    }

    private record SignalPair(int? Snr, int? Rssi, long Count);
}
