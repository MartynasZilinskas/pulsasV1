using Funcky;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NodaTime;
using Npgsql;
using Pulsas.Core.Geography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Payloads.Adverts;
using static Pulsas.Persistence.Nodes.IAdvertNodeManager;

namespace Pulsas.Persistence.Nodes;

// A node projection only: packet/reception evidence is deliberately not fabricated.
public class AdvertNodeManager(IDbContextFactory<TrackingDbContext> databases, IClock clock)
    : IAdvertNodeManager
{
    private const int MaximumFutureSkewSeconds = 300;

    public async Task<Either<ProcessAdvertError, Unit>> ProcessAdvertAsync(
        ProcessAdvertInput input,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Interpretation records are constructible by callers; verify the signed bytes here.
        return await AdvertCodec
            .ValidateSignature(input.Advert)
            .Select(_ => ReadAcceptedMetadata(input))
            .Match(
                left: _ =>
                    Task.FromResult(
                        Either<ProcessAdvertError, Unit>.Left(ProcessAdvertError.InvalidSignature)
                    ),
                right: metadata => PersistAsync(input, metadata, cancellationToken)
            );
    }

    private async Task<Either<ProcessAdvertError, Unit>> PersistAsync(
        ProcessAdvertInput input,
        Option<Metadata> metadata,
        CancellationToken cancellationToken
    )
    {
        var advert = input.Advert;
        var now = clock.GetCurrentInstant();
        var key = advert.PublicKey.ToArray();
        await using var database = await databases.CreateDbContextAsync(cancellationToken);
        if (await database.MeshNodes.AnyAsync(node => node.PublicKey == key, cancellationToken))
        {
            await UpdateMetadataAsync(
                database,
                key,
                metadata,
                advert.Timestamp,
                now,
                cancellationToken
            );
            return Unit.Value;
        }

        return await CreateNodeAsync(
            database,
            key,
            metadata,
            advert.Timestamp,
            now,
            cancellationToken
        );
    }

    private async Task<Either<ProcessAdvertError, Unit>> CreateNodeAsync(
        TrackingDbContext database,
        byte[] key,
        Option<Metadata> metadata,
        long timestamp,
        Instant now,
        CancellationToken cancellationToken
    )
    {
        var node = new MeshNode
        {
            Id = GuidV7.Create(clock),
            PublicKey = key,
            CreatedAt = now,
            SourceType = MeshNodeSourceType.ObservedAdvert,
        };
        metadata.Switch(
            none: () => { },
            some: value =>
            {
                node.Name = value.Name;
                node.Role = value.Role;
                node.Location = value.Location;
                node.ModifiedAt = now;
                node.AdvertMetadataTimestamp = timestamp;
            }
        );
        database.MeshNodes.Add(node);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException
                    is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation,
                        SchemaName: "public",
                        TableName: "node_information",
                        ConstraintName: "IX_node_information_PublicKey",
                    }
            )
        {
            return Either<ProcessAdvertError, Unit>.Left(ProcessAdvertError.ConcurrentNodeCreation);
        }
    }

    private static Option<Metadata> ReadAcceptedMetadata(ProcessAdvertInput input) =>
        AdvertAppDataCodec
            .Decode(input.Advert.AppData.AsSpan())
            .RightOrNone()
            .Flatten()
            .Where(_ => IsWithinClockTolerance(input))
            .Where(HasStorableName)
            .SelectMany(ResolveMetadataLocation);

    private static bool IsWithinClockTolerance(ProcessAdvertInput input) =>
        input.Advert.Timestamp <= input.ArrivedAt.ToUnixTimeSeconds() + MaximumFutureSkewSeconds;

    // PostgreSQL text cannot contain NUL; do not partially apply this advert.
    private static bool HasStorableName(AdvertAppData claims) =>
        claims.Name?.Contains('\0') != true;

    private static Option<Metadata> ResolveMetadataLocation(AdvertAppData claims) =>
        Option
            .FromNullable(claims.LatitudeMicrodegrees)
            .Match(
                none: () => Option.Some(new Metadata(claims.Name, claims.Role, false, null)),
                // The codec guarantees that coordinates are both present or both absent.
                some: latitude =>
                    GeoLocation
                        .FromMicrodegrees(latitude, claims.LongitudeMicrodegrees!.Value)
                        .Select(location => new Metadata(
                            claims.Name,
                            claims.Role,
                            true,
                            AdvertLocation(location)
                        ))
            );

    private static Point? AdvertLocation(GeoLocation location) =>
        location switch
        {
            // Explicit zero-pair reset is ADVERT policy, not geographic validity.
            { LatitudeMicrodegrees: 0, LongitudeMicrodegrees: 0 } => null,
            _ => new Point(
                location.LongitudeMicrodegrees / 1e6,
                location.LatitudeMicrodegrees / 1e6
            )
            {
                SRID = 4326,
            },
        };

    private static Task UpdateMetadataAsync(
        TrackingDbContext database,
        byte[] key,
        Option<Metadata> metadata,
        long timestamp,
        Instant now,
        CancellationToken cancellationToken
    ) =>
        metadata.Match(
            none: () => Task.CompletedTask,
            some: value =>
                UpdateMetadataAsync(database, key, value, timestamp, now, cancellationToken)
        );

    private static Task UpdateMetadataAsync(
        TrackingDbContext database,
        byte[] key,
        Metadata metadata,
        long timestamp,
        Instant now,
        CancellationToken cancellationToken
    ) =>
        database
            .MeshNodes.Where(node =>
                node.PublicKey == key
                && (
                    node.AdvertMetadataTimestamp == null || node.AdvertMetadataTimestamp < timestamp
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(node => node.Name, node => metadata.Name ?? node.Name)
                        .SetProperty(node => node.Role, metadata.Role)
                        .SetProperty(
                            node => node.Location,
                            node => metadata.UpdatesLocation ? metadata.Location : node.Location
                        )
                        .SetProperty(node => node.ModifiedAt, now)
                        .SetProperty(node => node.AdvertMetadataTimestamp, timestamp),
                cancellationToken
            );

    private record Metadata(string? Name, NodeRole Role, bool UpdatesLocation, Point? Location);
}
