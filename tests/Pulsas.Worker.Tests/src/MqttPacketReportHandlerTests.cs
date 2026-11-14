using Microsoft.Extensions.Logging;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService;
using Xunit;

namespace Pulsas.Worker.Tests;

public class MqttPacketReportHandlerTests
{
    [Fact]
    public async Task Bad_report_does_not_prevent_next_generator_report_from_being_interpreted()
    {
        var logger = new CaptureLogger();
        var nodes = new CaptureAdvertNodeManager();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), nodes, logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");
        var topic = $"meshcore/KUN/{observer.PublicKey}/packets";
        await handler.HandleAsync(topic, "{"u8.ToArray(), at, CancellationToken.None);
        await handler.HandleAsync(
            "meshcore/KUN/invalid/packets",
            "{}"u8.ToArray(),
            at,
            CancellationToken.None
        );
        Assert.Empty(nodes.Inputs);
        using var sender = AdvertSigningKey.Generate();
        await handler.HandleAsync(
            topic,
            ObserverReports.Packet(
                observer,
                ObserverReports.CreateAdvert(sender, 1, at),
                at.AddMinutes(-10)
            ),
            at,
            CancellationToken.None
        );
        Assert.Contains(logger.Messages, message => message.Contains("MalformedJson"));
        Assert.Equal(at, Assert.Single(nodes.Inputs).ArrivedAt);
        Assert.Contains(logger.Messages, message => message.Contains("invalid topic"));
        Assert.Contains(logger.Messages, message =>
            message.Contains($"observer {observer.PublicKey}")
            && message.Contains($"advertiser {Convert.ToHexStringLower(sender.PublicKey.AsSpan())}")
            && message.Contains("signature Verified")
            && message.Contains("not stored")
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    public async Task Reports_without_decoded_adverts_are_logged_without_projection(string raw)
    {
        var logger = new CaptureLogger();
        var nodes = new CaptureAdvertNodeManager();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), nodes, logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");

        await handler.HandleAsync(
            $"meshcore/KUN/{observer.PublicKey}/packets",
            ObserverReports.Packet(observer, raw, at),
            at,
            CancellationToken.None
        );

        Assert.Empty(nodes.Inputs);
        Assert.Contains("Packet report interpreted", Assert.Single(logger.Messages));
    }

    [Fact]
    public async Task Disabled_information_logging_does_not_skip_processing()
    {
        var logger = new CaptureLogger(informationEnabled: false);
        var nodes = new CaptureAdvertNodeManager();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), nodes, logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");
        using var sender = AdvertSigningKey.Generate();

        await handler.HandleAsync(
            $"meshcore/KUN/{observer.PublicKey}/packets",
            ObserverReports.Packet(observer, ObserverReports.CreateAdvert(sender, 1, at), at),
            at,
            CancellationToken.None
        );

        Assert.Single(nodes.Inputs);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("", "Empty")]
    [InlineData("82", "Decoded")]
    [InlineData("82ff", "Malformed")]
    [InlineData("02ff", "Unsupported")]
    public async Task Logs_app_data_outcome_without_losing_verified_advert(
        string hex,
        string outcome
    )
    {
        var logger = new CaptureLogger();
        var nodes = new CaptureAdvertNodeManager();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), nodes, logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");
        using var sender = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(sender, 1, Convert.FromHexString(hex));
        var payload = Assert.IsType<byte[]>(
            AdvertCodec.Encode(advert).Match<object>(left: error => error, right: bytes => bytes)
        );
        var raw = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, payload)
        );
        await handler.HandleAsync(
            $"meshcore/KUN/{observer.PublicKey}/packets",
            ObserverReports.Packet(observer, Convert.ToHexString(raw), at),
            at,
            CancellationToken.None
        );
        var message = Assert.Single(logger.Messages);
        Assert.Contains("signature Verified", message);
        Assert.Contains($"metadata {outcome};", message);
        var forwarded = Assert.Single(nodes.Inputs).Advert;
        Assert.Equal(advert.PublicKey.ToArray(), forwarded.PublicKey.ToArray());
        Assert.Equal(advert.Timestamp, forwarded.Timestamp);
        Assert.Equal(advert.Signature.ToArray(), forwarded.Signature.ToArray());
        Assert.Equal(advert.AppData.ToArray(), forwarded.AppData.ToArray());
    }

    private class CaptureLogger(bool informationEnabled = true) : ILogger<MqttPacketReportHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.Information || informationEnabled;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
