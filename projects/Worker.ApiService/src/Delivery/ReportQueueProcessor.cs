using System.Text.Json;
using Funcky.Monads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgmq;
using Npgsql;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.PacketProcessing.Contracts.Ingestion;
using Pulsas.PacketProcessing.Contracts.Nodes;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.Persistence;
using static Pulsas.PacketProcessing.Contracts.Chat.IGroupTextManager;
using static Pulsas.PacketProcessing.Contracts.Ingestion.IReportIngestionManager;
using static Pulsas.PacketProcessing.Contracts.Nodes.IAdvertNodeManager;
using static Pulsas.Worker.ApiService.Delivery.IReportReceiver;

namespace Pulsas.Worker.ApiService.Delivery;

// Delivery owns the transaction, retry state and terminal evidence; managers make one attempt.
public class ReportQueueProcessor(
    TrackingDbContext database,
    IPacketProcessingManager interpretation,
    IReportIngestionManager ingestion,
    IAdvertNodeManager nodes,
    IGroupTextManager groupText,
    IOptions<ReportDeliveryOptions> options,
    ILogger<ReportQueueProcessor> logger,
    IPathAssessmentManager paths
) : IReportQueueProcessor
{
    private record Failure(string Reason, bool Retryable);

    public async Task<bool> ProcessNextAsync(string queueName, CancellationToken cancellationToken)
    {
        if (queueName != options.Value.IngestionQueue && queueName != options.Value.ContentQueue)
        {
            throw new ArgumentOutOfRangeException(nameof(queueName));
        }
        await database.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var queue = new NpgmqClient((NpgsqlConnection)database.Database.GetDbConnection());
        var message =
            queueName == options.Value.ContentQueue
                ? (
                    await queue.ReadGroupedHeadAsync<string>(queueName, 0, 1, cancellationToken)
                ).SingleOrDefault()
                : await queue.ReadAsync<string>(queueName, 0, cancellationToken);
        if (message is null)
        {
            return false;
        }

        // Claim/read count survives work rollback, so poison messages cannot retry forever.
        await transaction.CreateSavepointAsync("delivery_work", cancellationToken);
        Failure? failure;
        try
        {
            failure =
                queueName == options.Value.IngestionQueue
                    ? await IngestAsync(queue, message.Message!, cancellationToken)
                    : await ProcessContentAsync(message.Message!, cancellationToken);
            if (failure is null)
            {
                await ArchiveAsync(queue, queueName, message.MsgId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            failure = new("InvalidQueueEnvelope", false);
        }
        catch (Exception exception)
        {
            // Do not log exception messages: providers may include raw report/metadata values.
            failure = new(exception.GetType().Name, true);
        }

        if (failure is not null)
        {
            await transaction.RollbackToSavepointAsync("delivery_work", cancellationToken);
            database.ChangeTracker.Clear();
            if (!failure.Retryable || message.ReadCt >= options.Value.MaximumAttempts)
            {
                await queue.SendAsync(
                    options.Value.QuarantineQueue,
                    new QuarantinedReport(
                        queueName,
                        message.MsgId,
                        message.ReadCt,
                        failure.Reason,
                        message.Message ?? "null",
                        DateTimeOffset.UtcNow
                    ),
                    cancellationToken
                );
                await ArchiveAsync(queue, queueName, message.MsgId, cancellationToken);
                logger.LogWarning(
                    "Queue {Queue} message {MessageId} quarantined after {Attempts} attempts: {Reason}",
                    queueName,
                    message.MsgId,
                    message.ReadCt,
                    failure.Reason
                );
            }
            else
            {
                await queue.SetVtAsync(
                    queueName,
                    message.MsgId,
                    options.Value.RetryDelaySeconds,
                    cancellationToken
                );
                logger.LogWarning(
                    "Queue {Queue} message {MessageId} retry {Attempt}: {Reason}",
                    queueName,
                    message.MsgId,
                    message.ReadCt,
                    failure.Reason
                );
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Failure?> IngestAsync(
        NpgmqClient queue,
        string envelope,
        CancellationToken cancellationToken
    )
    {
        var input = JsonSerializer.Deserialize<ReceiveReportInput>(envelope);
        if (
            input is null
            || input.ReceptionId == Guid.Empty
            || input.OriginalReport.IsDefault
            || input.Topic is null
            || !Uri.TryCreate(input.BrokerEndpoint, UriKind.Absolute, out var broker)
            || broker.Scheme is not ("ws" or "wss")
            || !string.IsNullOrEmpty(broker.UserInfo)
        )
        {
            return new("InvalidQueueEnvelope", false);
        }
        var parts = input.Topic.Split('/');
        if (
            parts.Length != 4
            || parts[0] != "meshcore"
            || parts[3] != "packets"
            || string.IsNullOrEmpty(parts[1])
            || parts[1].IndexOfAny(['+', '#', '\0']) >= 0
            || parts[2].Length != 64
            || !parts[2].All(char.IsAsciiHexDigit)
        )
        {
            return new("InvalidTopic", false);
        }
        var stored = await interpretation
            .Process(
                new(
                    input.OriginalReport,
                    new ReportContext("mqtt", parts[1], parts[2], input.ArrivedAt)
                )
            )
            .Match(
                left: error =>
                    Task.FromResult(
                        Either<Failure, IngestReportResult>.Left(new(error.ToString(), false))
                    ),
                right: async result =>
                    (
                        await ingestion.IngestReportAsync(
                            new(input.ReceptionId, input.BrokerEndpoint, result),
                            cancellationToken
                        )
                    ).SelectLeft(error => new Failure(
                        error.ToString(),
                        error == IngestReportError.ConcurrentCreation
                    ))
            );
        return await stored.Match(
            left: error => Task.FromResult<Failure?>(error),
            right: async result =>
            {
                if (result.IsDuplicate)
                {
                    logger.LogInformation(
                        "Duplicate reception {ReceptionId} from observer {Observer} via broker {Broker} arriving at {ArrivedAt}",
                        result.ReceptionId,
                        parts[2].ToLowerInvariant(),
                        input.BrokerEndpoint,
                        input.ArrivedAt
                    );
                    return null;
                }
                await result.Content.Match(
                    none: () => Task.CompletedTask,
                    some: async content =>
                    {
                        var group =
                            $"{(byte)content.Type}:{content.PayloadVersion}:{Convert.ToHexStringLower(content.PayloadHash.AsSpan())}";
                        await queue.SendAsync(
                            options.Value.ContentQueue,
                            new ContentWork(result.ReceptionId),
                            new Dictionary<string, object> { ["x-pgmq-group"] = group },
                            cancellationToken
                        );
                    }
                );
                return null;
            }
        );
    }

    private async Task<Failure?> ProcessContentAsync(
        string envelope,
        CancellationToken cancellationToken
    )
    {
        var work = JsonSerializer.Deserialize<ContentWork>(envelope);
        if (work is null || work.ReceptionId == Guid.Empty)
        {
            return new("InvalidQueueEnvelope", false);
        }
        var reception = await database
            .MeshPacketReceptions.AsNoTracking()
            .Include(row => row.Packet)
                .ThenInclude(packet => packet!.Content)
            .SingleOrDefaultAsync(row => row.Id == work.ReceptionId, cancellationToken);
        if (reception?.Packet is not { } packet)
        {
            return new("MissingReceptionPacket", false);
        }
        if (
            await database.ReceptionPathAssessments.AnyAsync(
                row => row.ReceptionId == work.ReceptionId,
                cancellationToken
            )
        )
        {
            return null;
        }
        if (packet.Content.PayloadVersion != 0)
        {
            // Opaque/future content is preserved; no unsupported domain effect is invented.
            return await AssessPathAsync(work.ReceptionId, cancellationToken);
        }
        var failure = packet.Type switch
        {
            PacketType.GroupText => await ProcessGroupTextAsync(packet.Id, cancellationToken),
            PacketType.Advert => await ProcessAdvertAsync(
                packet.Content.Payload,
                reception.ArrivedAt.ToDateTimeOffset(),
                cancellationToken
            ),
            _ => null,
        };
        return failure ?? await AssessPathAsync(work.ReceptionId, cancellationToken);
    }

    private async Task<Failure?> AssessPathAsync(
        Guid receptionId,
        CancellationToken cancellationToken
    ) =>
        (await paths.AssessReceptionAsync(new(receptionId), cancellationToken)).Match<Failure?>(
            left: error =>
                error switch
                {
                    IPathAssessmentManager.AssessReceptionError.MissingReceptionPacket => new(
                        "MissingReceptionPacket",
                        false
                    ),
                    IPathAssessmentManager.AssessReceptionError.SaveFailed => new(
                        "PathAssessmentSaveFailed",
                        true
                    ),
                    null => throw new InvalidOperationException(
                        "Uninitialized path assessment error."
                    ),
                },
            right: _ => null
        );

    private async Task<Failure?> ProcessGroupTextAsync(
        Guid packetId,
        CancellationToken cancellationToken
    ) =>
        (await groupText.ProcessGroupTextAsync(new(packetId), cancellationToken)).Match<Failure?>(
            left: error => new(error.ToString(), error == ProcessGroupTextError.ConcurrentCreation),
            right: _ => null
        );

    private Task<Failure?> ProcessAdvertAsync(
        byte[] payload,
        DateTimeOffset arrivedAt,
        CancellationToken cancellationToken
    ) =>
        AdvertCodec
            .Decode(payload)
            .Match(
                left: error => Task.FromResult<Failure?>(new($"MalformedAdvert:{error}", false)),
                right: async advert =>
                    (
                        await nodes.ProcessAdvertAsync(new(advert, arrivedAt), cancellationToken)
                    ).Match<Failure?>(
                        left: error =>
                            new(
                                error.ToString(),
                                error == ProcessAdvertError.ConcurrentNodeCreation
                            ),
                        right: _ => null
                    )
            );

    private static async Task ArchiveAsync(
        NpgmqClient queue,
        string queueName,
        long id,
        CancellationToken cancellationToken
    )
    {
        if (!await queue.ArchiveAsync(queueName, id, cancellationToken))
        {
            throw new InvalidOperationException("Claimed queue message was not archived.");
        }
    }
}
