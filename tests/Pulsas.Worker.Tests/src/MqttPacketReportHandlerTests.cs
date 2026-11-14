using Funcky.Monads;
using Microsoft.Extensions.Logging;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService;
using Xunit;

namespace Pulsas.Worker.Tests;

public class MqttPacketReportHandlerTests
{
    [Fact]
    public void Bad_report_does_not_prevent_next_generator_report_from_being_interpreted()
    {
        var logger = new CaptureLogger();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");
        var topic = $"meshcore/KUN/{observer.PublicKey}/packets";
        handler.Handle(topic, "{"u8.ToArray(), at);
        handler.Handle("meshcore/KUN/invalid/packets", "{}"u8.ToArray(), at);
        using var sender = AdvertSigningKey.Generate();
        handler.Handle(
            topic,
            ObserverReports.Packet(observer, ObserverReports.CreateAdvert(sender, 1, at), at),
            at
        );
        Assert.Contains(logger.Messages, message => message.Contains("MalformedJson"));
        Assert.Contains(logger.Messages, message => message.Contains("invalid topic"));
        Assert.Contains(logger.Messages, message =>
            message.Contains($"observer {observer.PublicKey}")
            && message.Contains($"advertiser {Convert.ToHexStringLower(sender.PublicKey.AsSpan())}")
            && message.Contains("signature Verified")
            && message.Contains("not stored")
        );
    }

    [Fact]
    public void Disabled_information_logging_does_not_skip_processing()
    {
        var logger = new CaptureLogger(informationEnabled: false);
        var processing = new CountingProcessor();
        var handler = new MqttPacketReportHandler(processing, logger);
        var at = DateTimeOffset.UtcNow;
        var observer = new ObserverIdentity(new string('b', 64), "observer");
        using var sender = AdvertSigningKey.Generate();

        handler.Handle(
            $"meshcore/KUN/{observer.PublicKey}/packets",
            ObserverReports.Packet(observer, ObserverReports.CreateAdvert(sender, 1, at), at),
            at
        );

        Assert.Equal(1, processing.Calls);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("", "Empty")]
    [InlineData("82", "Decoded")]
    [InlineData("82ff", "Malformed")]
    [InlineData("02ff", "Unsupported")]
    public void Logs_app_data_outcome_without_losing_verified_advert(string hex, string outcome)
    {
        var logger = new CaptureLogger();
        var handler = new MqttPacketReportHandler(new PacketProcessingManager(), logger);
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
        handler.Handle(
            $"meshcore/KUN/{observer.PublicKey}/packets",
            ObserverReports.Packet(observer, Convert.ToHexString(raw), at),
            at
        );
        var message = Assert.Single(logger.Messages);
        Assert.Contains("signature Verified", message);
        Assert.Contains($"metadata {outcome}; not stored", message);
    }

    private class CountingProcessor : IPacketProcessingManager
    {
        public int Calls { get; private set; }

        public Either<ReportError, IPacketProcessingManager.Result> Process(
            IPacketProcessingManager.Input input
        )
        {
            Calls++;
            return new PacketProcessingManager().Process(input);
        }
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
