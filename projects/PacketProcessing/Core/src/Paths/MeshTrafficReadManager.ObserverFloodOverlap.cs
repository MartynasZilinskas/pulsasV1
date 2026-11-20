using System.Collections.Immutable;
using System.Data;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Pulsas.MeshCore.Packets;
using static Pulsas.PacketProcessing.Contracts.Paths.IMeshTrafficReadManager;

namespace Pulsas.PacketProcessing.Core.Paths;

public partial class MeshTrafficReadManager
{
    public async Task<
        Either<MissingOverlapObservers, FloodOverlapCounts>
    > ReadObserverFloodOverlapAsync(
        ObserverFloodOverlapInput input,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );

        var pair = new[] { input.ObserverAId, input.ObserverBId };
        var knownIds = await db
            .Observers.Where(observer => pair.Contains(observer.Id))
            .Select(observer => observer.Id)
            .ToArrayAsync(cancellationToken);
        ImmutableArray<Guid> missingIds = [.. pair.Except(knownIds)];
        if (!missingIds.IsEmpty)
        {
            return Either<MissingOverlapObservers, FloodOverlapCounts>.Left(new(missingIds));
        }

        var counts = await FilterReceptions(
                db.MeshPacketReceptions,
                new(input.From, input.To, [.. pair], input.RegionCodes, input.PacketTypes)
            )
            .Where(row =>
                row.Packet != null
                && (
                    row.Packet.RouteType == RouteType.Flood
                    || row.Packet.RouteType == RouteType.TransportFlood
                )
            )
            .GroupBy(row => row.PacketId!.Value)
            .Select(packet => new
            {
                HeardByA = packet.Max(row => row.ObserverId == input.ObserverAId ? 1 : 0) == 1,
                HeardByB = packet.Max(row => row.ObserverId == input.ObserverBId ? 1 : 0) == 1,
            })
            .GroupBy(packet => new { packet.HeardByA, packet.HeardByB })
            .Select(group => new { group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Either<MissingOverlapObservers, FloodOverlapCounts>.Right(
            new(
                counts.GetValueOrDefault(new { HeardByA = true, HeardByB = false }),
                counts.GetValueOrDefault(new { HeardByA = false, HeardByB = true }),
                counts.GetValueOrDefault(new { HeardByA = true, HeardByB = true })
            )
        );
    }
}
