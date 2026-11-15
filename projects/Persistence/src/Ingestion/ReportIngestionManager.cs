using System.Security.Cryptography;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using static Pulsas.Persistence.Ingestion.IReportIngestionManager;
using PacketOutcome = Pulsas.PacketProcessing.Contracts.Processing.PacketOutcome;
using PayloadInterpretation = Pulsas.PacketProcessing.Contracts.Processing.PayloadInterpretation;

namespace Pulsas.Persistence.Ingestion;

public class ReportIngestionManager(TrackingDbContext database, IClock clock)
    : IReportIngestionManager
{
    public async Task<Either<IngestReportError, MeshPacketReception>> IngestReportAsync(
        IngestReportInput input,
        CancellationToken cancellationToken
    )
    {
        var interpreted = input.Interpreted;
        var region = await database.Regions.SingleOrDefaultAsync(
            item => item.Code == interpreted.Input.Context.Region,
            cancellationToken
        );
        if (region is null)
        {
            return Either<IngestReportError, MeshPacketReception>.Left(
                IngestReportError.UnknownRegion
            );
        }

        var now = clock.GetCurrentInstant();
        var observerKey = Convert.FromHexString(interpreted.Report.Observer);
        var observer = await database.Observers.SingleOrDefaultAsync(
            item => item.PublicKey == observerKey,
            cancellationToken
        );
        var newObserver = observer is null;
        observer ??= new StoredObserver
        {
            Id = GuidV7.Create(clock),
            PublicKey = observerKey,
            CreatedAt = now,
            ModifiedAt = now,
        };

        var broker = await database.Brokers.SingleOrDefaultAsync(
            item => item.Endpoint == input.BrokerEndpoint,
            cancellationToken
        );
        var newBroker = broker is null;
        broker ??= new StoredBroker
        {
            Id = GuidV7.Create(clock),
            Endpoint = input.BrokerEndpoint,
            Name = input.BrokerEndpoint,
            CreatedAt = now,
            ModifiedAt = now,
        };

        var outcome = interpreted.Packet;
        byte[]? raw = outcome switch
        {
            PacketOutcome.Decoded decoded => decoded.Raw.ToArray(),
            PacketOutcome.Malformed malformed => malformed.Raw.ToArray(),
            _ => null,
        };
        MeshPacket? packet = null;
        var decodeError = outcome switch
        {
            PacketOutcome.Absent => "absent",
            PacketOutcome.InvalidHex => "invalid_hex",
            PacketOutcome.Malformed malformed => $"malformed:{malformed.Error}",
            _ => null,
        };

        if (outcome is PacketOutcome.Decoded decodedPacket)
        {
            var corePacket = decodedPacket.Packet;
            var payload = corePacket.Payload.ToArray();
            var payloadHash = SHA256.HashData(payload);
            var content = await database.PacketContents.SingleOrDefaultAsync(
                item =>
                    item.Type == corePacket.PacketType
                    && item.PayloadVersion == corePacket.PayloadVersion
                    && item.PayloadHash == payloadHash,
                cancellationToken
            );
            if (content is null)
            {
                content = new StoredPacketContent
                {
                    Id = GuidV7.Create(clock),
                    Type = corePacket.PacketType,
                    PayloadVersion = (short)corePacket.PayloadVersion,
                    PayloadHash = payloadHash,
                    Payload = payload,
                };
                database.PacketContents.Add(content);
            }

            var packetHash = SHA256.HashData(raw!);
            packet = await database.MeshPackets.SingleOrDefaultAsync(
                item => item.PacketHash == packetHash,
                cancellationToken
            );
            if (packet is null)
            {
                packet = new MeshPacket
                {
                    Id = GuidV7.Create(clock),
                    ContentId = content.Id,
                    Content = content,
                    PacketHash = packetHash,
                    ShortHash = Convert
                        .ToBase64String(packetHash)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_'),
                    PayloadHash = payloadHash,
                    Type = corePacket.PacketType,
                    RouteType = corePacket.RouteType,
                    TransportCodes = corePacket.HasTransportCodes
                        ? corePacket.TransportCodes.ToArray()
                        : null,
                    PathPrefixWidth = (short)corePacket.PathWidth,
                    RawPacket = raw!,
                    LastPathStepId = null,
                    SignatureVerification = SignatureVerification(decodedPacket),
                };
                database.MeshPackets.Add(packet);
            }
            else
            {
                packet.Content = content;
            }
        }

        var reception = new MeshPacketReception
        {
            Id = input.ReceptionId,
            Observer = observer,
            Broker = broker,
            Packet = packet,
            PacketId = packet?.Id,
            Region = region,
            RegionCode = region.Code,
            ArrivedAt = Instant.FromDateTimeOffset(interpreted.Input.Context.ArrivedAt),
            HeardAt = null,
            Signal = new ReceptionSignal
            {
                Rssi = interpreted.Report.Rssi,
                Snr = interpreted.Report.Snr,
            },
            RawPacket = raw,
            DecodeError = decodeError,
            Association = PacketAssociation.PublisherAssertedRx,
        };
        if (newObserver)
        {
            database.Observers.Add(observer);
        }
        if (newBroker)
        {
            database.Brokers.Add(broker);
        }
        database.MeshPacketReceptions.Add(reception);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return reception;
        }
        catch (DbUpdateException exception) when (IsConcurrentCreation(exception))
        {
            return Either<IngestReportError, MeshPacketReception>.Left(
                IngestReportError.ConcurrentCreation
            );
        }
    }

    private AdvertSignatureVerification SignatureVerification(PacketOutcome.Decoded decoded)
    {
        if (decoded.Interpretation is not PayloadInterpretation.Advert advert)
        {
            return new AdvertSignatureVerification();
        }

        return AdvertCodec
            .ValidateSignature(advert.Payload)
            .Match(
                left: error => new AdvertSignatureVerification
                {
                    Status = AdvertSignatureStatus.InvalidSignature,
                    FailureReason = error.Kind,
                    CheckedAt = clock.GetCurrentInstant(),
                },
                right: _ => new AdvertSignatureVerification
                {
                    Status = AdvertSignatureStatus.Verified,
                    CheckedAt = clock.GetCurrentInstant(),
                }
            );
    }

    private static bool IsConcurrentCreation(DbUpdateException exception) =>
        exception.InnerException
            is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                SchemaName: "public",
            } postgres
        && (postgres.TableName, postgres.ConstraintName)
            is
                ("observers", "ux_observers_key")
                or
                ("brokers", "ux_brokers_endpoint")
                or
                ("packets", "ux_packets_hash")
                or
                ("packet_contents", "IX_packet_contents_Type_PayloadVersion_PayloadHash");
}
