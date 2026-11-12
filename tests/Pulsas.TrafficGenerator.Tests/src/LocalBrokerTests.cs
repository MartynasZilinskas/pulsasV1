using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;

namespace Pulsas.TrafficGenerator.Tests;

public class LocalBrokerTests
{
    [LocalBrokerFact]
    public async Task Initial_connection_failure_can_retry_and_idle_connection_lasts_until_host_shutdown()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var manager = new TrafficGeneratorManager(
            Options.Create(
                new TrafficGeneratorOptions
                {
                    Endpoint = $"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/mqtt",
                    ObserverCount = 1,
                    Rounds = 1,
                }
            ),
            NullLogger<TrafficGeneratorManager>.Instance
        );
        var startup = ((IHostedService)manager).StartAsync(token);
        using (var rejected = await listener.AcceptTcpClientAsync(token))
        {
            // An explicit HTTP rejection avoids ClientWebSocket retrying an EOF
            // and leaving a second startup attempt in the listener backlog.
            var request = new byte[4096];
            Assert.True(await rejected.GetStream().ReadAsync(request, token) > 0);
            await rejected
                .GetStream()
                .WriteAsync(
                    "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
                    token
                );
        }
        await startup;
        Assert.Equal("Failed", manager.Status.State);
        Assert.False(manager.Status.Connected);
        Assert.Equal(0, manager.Status.PublishedStatusReports);

        // On retry, relay the real WebSocket stream to the local broker.
        await manager.StartAsync(token);
        using var connection = await listener.AcceptTcpClientAsync(token);
        using var upstream = new TcpClient();
        await upstream.ConnectAsync("localhost", 9001, token);
        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var outbound = connection
            .GetStream()
            .CopyToAsync(upstream.GetStream(), relayCancellation.Token);
        var inbound = upstream
            .GetStream()
            .CopyToAsync(connection.GetStream(), relayCancellation.Token);
        try
        {
            while (manager.Status.State == "Running")
            {
                await Task.Delay(10, token);
            }

            Assert.Equal("Completed", manager.Status.State);
            Assert.True(manager.Status.Connected);
            Assert.Null(manager.Status.Error);
            Assert.Equal(1, manager.Status.PublishedPacketReports);
            Assert.Equal(2, manager.Status.PublishedStatusReports);
            await manager.StopAsync(token);
            Assert.True(manager.Status.Connected);
            await ((IHostedService)manager).StopAsync(token);
            Assert.False(manager.Status.Connected);
        }
        finally
        {
            relayCancellation.Cancel();
            try
            {
                await Task.WhenAll(outbound, inbound);
            }
            catch (OperationCanceledException) when (relayCancellation.IsCancellationRequested) { }
        }
    }

    [LocalBrokerFact]
    public async Task Dashboard_endpoints_publish_valid_reports_and_control_one_run()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5300") };
        using var subscriber = new MqttClientFactory().CreateMqttClient();
        var messages = Channel.CreateUnbounded<MqttApplicationMessage>();
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            messages.Writer.TryWrite(args.ApplicationMessage);
            return Task.CompletedTask;
        };
        await subscriber.ConnectAsync(
            new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithClientId($"verify-{Guid.NewGuid():N}"[..23])
                .WithCleanSession()
                .WithWebSocketServer(server => server.WithUri("ws://localhost:9001/mqtt"))
                .Build(),
            token
        );
        var subscription = await subscriber.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter =>
                    filter
                        .WithTopic("meshcore/VNO/+/+")
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                )
                .Build(),
            token
        );
        Assert.Equal(2, (int)subscription.Items.Single().ResultCode);
        try
        {
            Assert.Equal("Idle", (await Status()).State);
            Assert.True((await Status()).Connected);
            await Task.Delay(200, token);
            Assert.False(messages.Reader.TryRead(out _));
            var starts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Command("start")));
            Assert.Single(starts.Select(status => status.RunId).Distinct());
            var completed = await WaitFor(status => status.State is "Completed" or "Failed");
            Assert.Equal("Completed", completed.State);
            Assert.True(completed.Connected);
            Assert.Equal(10, completed.CompletedRounds);
            Assert.Equal(30, completed.PublishedPacketReports);
            Assert.Equal(6, completed.PublishedStatusReports);

            var packets = new Dictionary<string, HashSet<string>>();
            var online = new HashSet<string>();
            var offline = new HashSet<string>();
            for (var i = 0; i < 36; i++)
            {
                var message = await messages.Reader.ReadAsync(token);
                Assert.False(message.Retain);
                using var json = JsonDocument.Parse(message.ConvertPayloadToString());
                var root = json.RootElement;
                var parts = message.Topic.Split('/');
                var observer = root.GetProperty("origin_id").GetString()!;
                Assert.Equal(observer, parts[2]);
                Assert.Equal(32, Convert.FromHexString(observer).Length);
                if (parts[3] == "status")
                {
                    var state = root.GetProperty("status").GetString();
                    Assert.Contains(state, new[] { "online", "offline" });
                    (state == "online" ? online : offline).Add(observer);
                    continue;
                }
                Assert.Equal("packets", parts[3]);
                Assert.Equal("PACKET", root.GetProperty("type").GetString());
                Assert.Equal("rx", root.GetProperty("direction").GetString());
                var raw = root.GetProperty("raw").GetString()!;
                var packet = Assert.IsType<MeshCorePacket>(
                    PacketCodec
                        .Decode(Convert.FromHexString(raw))
                        .Match<object>(left: error => error, right: packet => packet)
                );
                Assert.Equal(PacketType.Advert, packet.PacketType);
                var advert = AdvertCodec.Decode(packet.Payload.AsSpan());
                Assert.True(AdvertCodec.Verify(advert));
                Assert.NotEqual(observer, Convert.ToHexStringLower(advert.PublicKey.AsSpan()));
                if (!packets.TryGetValue(raw, out var reporters))
                {
                    packets.Add(raw, reporters = []);
                }

                Assert.True(reporters.Add(observer));
            }
            Assert.Equal(10, packets.Count);
            Assert.Equal(3, online.Count);
            Assert.True(online.SetEquals(offline));
            Assert.All(packets.Values, reporters => Assert.True(online.SetEquals(reporters)));

            var restart = await Command("start");
            Assert.NotEqual(completed.RunId, restart.RunId);
            await WaitFor(status => status.PublishedPacketReports >= 3 || status.State == "Failed");
            var stops = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Command("stop")));
            Assert.All(stops, stopped => Assert.Equal("Stopped", stopped.State));
            Assert.Equal("Stopped", (await Command("stop")).State);
            Assert.True((await Status()).Connected);
            var stoppedCount = (await Status()).PublishedPacketReports;
            await Task.Delay(200, token);
            Assert.Equal(stoppedCount, (await Status()).PublishedPacketReports);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await http.PostAsync("/traffic/stop", null, cleanup.Token);
            response.EnsureSuccessStatusCode();
            await subscriber.DisconnectAsync(new MqttClientDisconnectOptions(), cleanup.Token);
        }

        async Task<TrafficStatus> Status() =>
            (await http.GetFromJsonAsync<TrafficStatus>("/traffic/status", token))!;

        async Task<TrafficStatus> Command(string command)
        {
            using var response = await http.PostAsync($"/traffic/{command}", null, token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TrafficStatus>(token))!;
        }

        async Task<TrafficStatus> WaitFor(Func<TrafficStatus, bool> predicate)
        {
            while (true)
            {
                var status = await Status();
                if (predicate(status))
                {
                    return status;
                }

                await Task.Delay(20, token);
            }
        }
    }
}
