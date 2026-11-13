using System.Text.Json;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;

namespace Pulsas.TrafficGenerator.Tests;

public class ObserverReportsTests
{
    [Fact]
    public void Observers_report_one_independently_signed_packet_with_distinct_rounds()
    {
        var observers = ObserverReports.CreateObservers(3, CancellationToken.None);
        using var sender = AdvertSigningKey.Generate();
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var raw = ObserverReports.CreateAdvert(sender, 0, at);
        var advert = Assert.IsType<AdvertPayload>(
            PacketCodec
                .Decode(Convert.FromHexString(raw))
                .Select(packet =>
                    AdvertCodec
                        .Decode(packet.Payload.AsSpan())
                        .Match<object>(left: error => error, right: advert => advert)
                )
                .Match<object>(left: error => error, right: advert => advert)
        );
        Assert.True(AdvertCodec.Verify(advert));
        Assert.Equal(1_700_000_000u, advert.Timestamp);
        Assert.NotEqual(raw, ObserverReports.CreateAdvert(sender, 1, at));
        Assert.Equal(3, observers.Select(observer => observer.PublicKey).Distinct().Count());
        foreach (var observer in observers)
        {
            using var report = JsonDocument.Parse(ObserverReports.Packet(observer, raw, at));
            var root = report.RootElement;
            Assert.Equal(observer.PublicKey, root.GetProperty("origin_id").GetString());
            Assert.NotEqual(
                Convert.ToHexStringLower(advert.PublicKey.AsSpan()),
                observer.PublicKey
            );
            Assert.Equal(raw, root.GetProperty("raw").GetString());
            Assert.Equal("PACKET", root.GetProperty("type").GetString());
            Assert.Equal("rx", root.GetProperty("direction").GetString());
            Assert.Equal(at, root.GetProperty("timestamp").GetDateTimeOffset());
            Assert.True(root.TryGetProperty("RSSI", out _));
            Assert.True(root.TryGetProperty("SNR", out _));
        }
    }

    [Theory]
    [InlineData("mqtt://localhost:1883", "VNO", 1, 1, 0, 1)]
    [InlineData("ws://localhost:9001/mqtt", "V/+#", 1, 1, 0, 1)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", 0, 1, 0, 1)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", 1, 0, 0, 1)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", 1, 1, -1, 1)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", 1, 1, 0, 3)]
    public void Rejects_invalid_configuration(
        string endpoint,
        string region,
        int observers,
        int rounds,
        int interval,
        int qos
    ) =>
        Assert.False(
            new TrafficGeneratorOptions
            {
                Endpoint = endpoint,
                Region = region,
                ObserverCount = observers,
                Rounds = rounds,
                IntervalMilliseconds = interval,
                Qos = qos,
            }.IsValid()
        );

    [Theory]
    [InlineData("ws://localhost:9001/mqtt", "VNO", 0)]
    [InlineData("wss://broker.example/mqtt", "KUN", 2)]
    public void Accepts_supported_transport_and_qos(string endpoint, string region, int qos) =>
        Assert.True(
            new TrafficGeneratorOptions
            {
                Endpoint = endpoint,
                Region = region,
                Qos = qos,
            }.IsValid()
        );
}
