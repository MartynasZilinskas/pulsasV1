using Funcky;
using Funcky.Monads;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.Persistence.Nodes;
using static Pulsas.Persistence.Nodes.IAdvertNodeManager;

namespace Pulsas.Worker.ApiService;

public class MqttPacketReportHandler(
    IPacketProcessingManager processing,
    IAdvertNodeManager nodes,
    ILogger<MqttPacketReportHandler> logger
)
{
    public async Task<Either<ProcessAdvertError, Unit>> HandleAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset arrivedAt,
        CancellationToken cancellationToken
    )
    {
        var parts = topic.Split('/');
        if (
            parts.Length != 4
            || parts[0] != "meshcore"
            || string.IsNullOrWhiteSpace(parts[1])
            || parts[2].Length != 64
            || !parts[2].All(char.IsAsciiHexDigit)
            || parts[3] != "packets"
        )
        {
            logger.LogWarning("MQTT packet report rejected: invalid topic.");
            return Either<ProcessAdvertError, Unit>.Right(default);
        }

        return await processing
            .Process(
                new([.. payload.Span], new ReportContext("mqtt", parts[1], parts[2], arrivedAt))
            )
            .Match(
                left: error =>
                {
                    logger.LogWarning("MQTT packet report rejected: {Reason}", error);
                    return Task.FromResult(Either<ProcessAdvertError, Unit>.Right(default));
                },
                right: interpreted => ProjectAndLogAsync(interpreted, cancellationToken)
            );
    }

    private async Task<Either<ProcessAdvertError, Unit>> ProjectAndLogAsync(
        IPacketProcessingManager.Result interpreted,
        CancellationToken cancellationToken
    )
    {
        var projection = Either<ProcessAdvertError, Unit>.Right(default);
        if (
            interpreted.Packet is PacketOutcome.Decoded
            {
                Interpretation: PayloadInterpretation.Advert advert
            }
        )
        {
            projection = await nodes.ProcessAdvertAsync(
                new(advert.Payload, interpreted.Input.Context.ArrivedAt),
                cancellationToken
            );
        }
        LogResult(interpreted);
        return projection;
    }

    private void LogResult(IPacketProcessingManager.Result result)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var packet = result.Packet;
        var decoded = packet as PacketOutcome.Decoded;
        var advert = decoded?.Interpretation as PayloadInterpretation.Advert;
        logger.LogInformation(
            "Packet report interpreted: observer {Observer}, region {Region}, packet {Packet}, type {PacketType}, payload {Interpretation}, advertiser {Advertiser}, signature {Signature}, metadata {Metadata}; packet/reception not stored",
            result.Report.Observer,
            result.Input.Context.Region,
            packet.GetType().Name,
            decoded?.Packet.PacketType,
            decoded?.Interpretation.GetType().Name,
            advert is null ? null : Convert.ToHexStringLower(advert.Payload.PublicKey.AsSpan()),
            advert?.Signature,
            advert?.AppData.Match(
                left: error => error.Kind.ToString(),
                right: data => data.Match(none: () => "Empty", some: _ => "Decoded")
            )
        );
    }
}
