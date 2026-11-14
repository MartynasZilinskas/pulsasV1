using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;

namespace Pulsas.Worker.ApiService;

public class MqttPacketReportHandler(
    IPacketProcessingManager processing,
    ILogger<MqttPacketReportHandler> logger
)
{
    public void Handle(string topic, ReadOnlyMemory<byte> payload, DateTimeOffset arrivedAt)
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
            return;
        }

        var result = processing.Process(
            new([.. payload.Span], new ReportContext("mqtt", parts[1], parts[2], arrivedAt))
        );
        result.Switch(
            left: error => logger.LogWarning("MQTT packet report rejected: {Reason}", error),
            right: LogResult
        );
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
            "Packet report interpreted in memory: observer {Observer}, region {Region}, packet {Packet}, type {PacketType}, payload {Interpretation}, advertiser {Advertiser}, signature {Signature}, metadata {Metadata}; not stored",
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
