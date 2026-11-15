using System.Collections.Concurrent;
using System.Reflection;
using Funcky;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Pulsas.MeshCore.Cryptography;
using Pulsas.TrafficGenerator;
using Pulsas.Worker.ApiService;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;
using static Pulsas.Worker.ApiService.Delivery.IReportReceiver;

namespace Pulsas.Worker.Tests;

public class DummyMqttTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("canceled")]
    public async Task Callback_awaits_durable_acceptance_and_only_acknowledges_on_success(
        string outcome
    )
    {
        var acceptance = new TaskCompletionSource<Unit>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receiver = new PendingAcceptance(acceptance.Task);
        using var consumer = new DummyMqttConsumer(
            Options.Create(new DummyMqttOptions()),
            new CaptureLogger<DummyMqttConsumer>(),
            receiver
        );
        var message = new MqttApplicationMessageBuilder()
            .WithTopic("meshcore/VNO/test/packets")
            .WithPayload([1, 2, 3])
            .Build();
        var args = new MqttApplicationMessageReceivedEventArgs(
            "test",
            message,
            new MqttPublishPacket(),
            (_, _) => Task.CompletedTask
        );
        // Invoke the real callback without starting a broker or changing its public API.
        var callback = typeof(DummyMqttConsumer).GetMethod(
            "OnMessageReceivedAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var handling = (Task)callback.Invoke(consumer, [args, CancellationToken.None])!;
        Assert.False(handling.IsCompleted);
        if (outcome == "success")
        {
            acceptance.SetResult(Unit.Value);
            await handling;
            Assert.True(args.AutoAcknowledge);
        }
        else if (outcome == "failure")
        {
            var failure = new InvalidOperationException("acceptance failed");
            acceptance.SetException(failure);
            Assert.Same(
                failure,
                await Assert.ThrowsAsync<InvalidOperationException>(() => handling)
            );
            Assert.False(args.AutoAcknowledge);
        }
        else if (outcome == "canceled")
        {
            acceptance.SetCanceled();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handling);
            Assert.False(args.AutoAcknowledge);
        }
    }

    private class PendingAcceptance(Task<Unit> completion) : IReportReceiver
    {
        public Task<Unit> ReceiveReportAsync(
            ReceiveReportInput input,
            CancellationToken cancellationToken
        ) => completion;
    }

    [Theory]
    [InlineData("mqtt://localhost:1883", "VNO", false)]
    [InlineData("ws://user:password@localhost/mqtt", "VNO", false)]
    [InlineData("ws://localhost:9001/mqtt#fragment", "VNO", false)]
    [InlineData("ws://localhost:9001/mqtt", "V/+", false)]
    [InlineData("ws://localhost:9001/mqtt", "vno", true)]
    [InlineData("ws://localhost:9001/mqtt", "east-lithuania", true)]
    [InlineData("ws://localhost:9001/mqtt", "a#b", false)]
    [InlineData("ws://localhost:9001/mqtt", " east", false)]
    [InlineData("ws://localhost:9001/mqtt", "", false)]
    [InlineData("ws://localhost:9001/mqtt", "a\0b", false)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", true)]
    [InlineData("wss://broker.example/mqtt", "KUN", true)]
    public void Validates_configuration(string endpoint, string region, bool expected) =>
        Assert.Equal(
            expected,
            new DummyMqttOptions { Endpoint = endpoint, Region = region }.IsValid()
        );

    [Theory]
    [InlineData("VNO", true)]
    [InlineData("east-lithuania", true)]
    [InlineData("vno", true)]
    [InlineData("", false)]
    [InlineData("east/south", false)]
    [InlineData("east+", false)]
    [InlineData("east#", false)]
    public void Generator_uses_generic_topic_safe_regions(string region, bool expected) =>
        Assert.Equal(expected, new TrafficGeneratorOptions { Region = region }.IsValid());

    [WorkerBrokerFact]
    public async Task Subscribes_receives_both_topics_and_disconnects_on_shutdown()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var endpoint = Environment.GetEnvironmentVariable("PULSAS_WORKER_MQTT_ENDPOINT")!;
        Assert.True(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri));
        Assert.True(uri.IsLoopback, "This opt-in test is restricted to a local broker.");
        var logger = new CaptureLogger<DummyMqttConsumer>();
        var received = new CaptureReports();
        using var consumer = new DummyMqttConsumer(
            Options.Create(new DummyMqttOptions { Endpoint = endpoint, Region = "TST" }),
            logger,
            received
        );
        var context = new HealthCheckContext();
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await consumer.CheckHealthAsync(context, token)).Status
        );
        await consumer.StartAsync(token);
        try
        {
            while ((await consumer.CheckHealthAsync(context, token)).Status != HealthStatus.Healthy)
            {
                await Task.Delay(25, token);
            }

            using var publisher = new MqttClientFactory().CreateMqttClient();
            await publisher.ConnectAsync(
                new MqttClientOptionsBuilder()
                    .WithProtocolVersion(MqttProtocolVersion.V311)
                    .WithWebSocketServer(server => server.WithUri(endpoint))
                    .Build(),
                token
            );
            var observer = Guid.NewGuid().ToString("N");
            foreach (var kind in new[] { "status", "packets" })
            {
                var topic = $"meshcore/TST/{observer}/{kind}";
                await publisher.PublishAsync(
                    new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload("synthetic")
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build(),
                    token
                );
                while (
                    !logger.Messages.Any(message =>
                        message.Contains($"Dummy MQTT received {topic}: 9 bytes")
                    )
                )
                {
                    await Task.Delay(25, token);
                }
            }
            using var sender = AdvertSigningKey.Generate();
            var at = DateTimeOffset.UtcNow;
            var identity = new ObserverIdentity(new string('a', 64), "synthetic-observer");
            await publisher.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic($"meshcore/TST/{identity.PublicKey}/packets")
                    .WithPayload(
                        ObserverReports.Packet(
                            identity,
                            ObserverReports.CreateAdvert(sender, 1, at),
                            at
                        )
                    )
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                token
            );
            while (!received.Inputs.Any(input => input.Topic.Contains(identity.PublicKey)))
            {
                await Task.Delay(25, token);
            }
            await publisher.DisconnectAsync(new MqttClientDisconnectOptions(), token);
        }
        finally
        {
            await consumer.StopAsync(token);
        }
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await consumer.CheckHealthAsync(context, token)).Status
        );
    }

    private class CaptureReports : IReportReceiver
    {
        public ConcurrentQueue<ReceiveReportInput> Inputs { get; } = new();

        public Task<Unit> ReceiveReportAsync(
            ReceiveReportInput input,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Enqueue(input);
            return Task.FromResult(Unit.Value);
        }
    }

    private class WorkerBrokerFactAttribute : FactAttribute
    {
        public WorkerBrokerFactAttribute()
        {
            if (
                string.IsNullOrEmpty(
                    Environment.GetEnvironmentVariable("PULSAS_WORKER_MQTT_ENDPOINT")
                )
            )
            {
                Skip = "Set PULSAS_WORKER_MQTT_ENDPOINT to an isolated local WebSocket broker.";
            }
        }
    }

    private class CaptureLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Enqueue(formatter(state, exception));
    }
}
