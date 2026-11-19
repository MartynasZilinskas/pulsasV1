using System.Security.Cryptography;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Ingestion;
using Pulsas.Persistence;
using Pulsas.Persistence.Brokers;
using Pulsas.Persistence.Observers;
using Pulsas.Persistence.Packets;
using Pulsas.Persistence.Receptions;
using static Pulsas.PacketProcessing.Contracts.Ingestion.IReportIngestionManager;
using PacketOutcome = Pulsas.PacketProcessing.Contracts.Processing.PacketOutcome;
using PayloadInterpretation = Pulsas.PacketProcessing.Contracts.Processing.PayloadInterpretation;

namespace Pulsas.PacketProcessing.Core.Ingestion;

public class ReportIngestionManager(TrackingDbContext database, IClock clock)
    : IReportIngestionManager
{
    public async Task<Either<IngestReportError, IngestReportResult>> IngestReportAsync(
        IngestReportInput input,
        CancellationToken cancellationToken
    )
    {
        var interpreted = input.Interpreted;
        var outcome = interpreted.Packet;
        byte[]? raw = outcome switch
        {
            PacketOutcome.Decoded decoded => [.. decoded.Raw],
            PacketOutcome.Malformed malformed => [.. malformed.Raw],
            _ => null,
        };
        var identity = raw is null ? null : ReceptionIdentity.Create(interpreted.Report, raw);
        if (identity is not null)
        {
            var existing = await database.MeshPacketReceptions.SingleOrDefaultAsync(
                item => item.ReceptionIdentity == identity,
                cancellationToken
            );
            if (existing is not null)
            {
                return new IngestReportResult(
                    existing.Id,
                    IsDuplicate: true,
                    Option<PacketContentIdentity>.None
                );
            }
        }
        var region = await database.Regions.SingleOrDefaultAsync(
            item => item.Code == interpreted.Input.Context.Region,
            cancellationToken
        );
        if (region is null)
        {
            return Either<IngestReportError, IngestReportResult>.Left(
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
            else if (!content.Payload.AsSpan().SequenceEqual(payload))
            {
                throw new InvalidOperationException("Content digest collision.");
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
                if (!packet.RawPacket.AsSpan().SequenceEqual(raw))
                {
                    throw new InvalidOperationException("Packet digest collision.");
                }
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
            ReportTimestamp = new ReceptionReportTimestamp
            {
                Status = interpreted.Report.Timestamp.Status,
                Claim = interpreted.Report.Timestamp.Claim,
            },
            ReceptionIdentity = identity,
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
            var contentIdentity = Option
                .FromNullable(packet)
                .Select(storedPacket => new PacketContentIdentity(
                    storedPacket.Type,
                    storedPacket.Content.PayloadVersion,
                    [.. storedPacket.Content.PayloadHash]
                ));
            return new IngestReportResult(reception.Id, IsDuplicate: false, contentIdentity);
        }
        catch (DbUpdateException exception) when (IsConcurrentCreation(exception))
        {
            return Either<IngestReportError, IngestReportResult>.Left(
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
        PostgresWriteErrors.PublicUniqueViolation(exception)
            is
                ("observers", "ux_observers_key")
                or
                ("brokers", "ux_brokers_endpoint")
                or
                ("packets", "ux_packets_hash")
                or
                ("packet_receptions", "ux_receptions_identity")
                or
                ("packet_contents", "IX_packet_contents_Type_PayloadVersion_PayloadHash");
}
