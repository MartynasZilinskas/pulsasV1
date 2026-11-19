using System.Collections.Immutable;
using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.Persistence;
using Pulsas.Persistence.Chat;
using static Pulsas.PacketProcessing.Contracts.Chat.IChannelCatalogueManager;

namespace Pulsas.PacketProcessing.Core.Chat;

public class ChannelCatalogueManager(
    TrackingDbContext database,
    ImmutableArray<ConfiguredChannel> channels,
    IClock clock
) : IChannelCatalogueManager
{
    public async Task<Either<ReconcileChannelsError, Unit>> ReconcileChannelsAsync(
        CancellationToken cancellationToken
    )
    {
        var rows = await database.Channels.ToListAsync(cancellationToken);
        foreach (var configured in channels)
        {
            var row = rows.SingleOrDefault(existing => HasIdentity(existing, configured));
            if (row is null)
            {
                row = new Channel
                {
                    Id = GuidV7.Create(clock),
                    Kind = configured.Kind,
                    ConfigurationId = configured.ConfigurationId,
                    Name = configured.Name,
                };
                database.Channels.Add(row);
                rows.Add(row);
            }
            row.Name = configured.Name;
            row.Active = true;
            row.Public = configured.Public;
        }
        foreach (var row in rows)
        {
            if (!channels.Any(configured => HasIdentity(row, configured)))
            {
                row.Active = false;
                row.Public = false;
            }
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
        catch (DbUpdateException exception)
            when (PostgresWriteErrors.PublicUniqueViolation(exception)
                    is (
                        "channels",
                        "IX_channels_Kind"
                            or "IX_channels_Name"
                            or "IX_channels_ConfigurationId"
                    )
            )
        {
            return Either<ReconcileChannelsError, Unit>.Left(
                ReconcileChannelsError.ConcurrentChannelCreation
            );
        }
    }

    private static bool HasIdentity(Channel row, ConfiguredChannel configured) =>
        row.Kind == configured.Kind
        && configured.Kind switch
        {
            ChannelKind.StandardPublic => true,
            ChannelKind.Hashtag => row.Name == configured.Name,
            ChannelKind.ExplicitKey => string.Equals(
                row.ConfigurationId,
                configured.ConfigurationId,
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };
}
