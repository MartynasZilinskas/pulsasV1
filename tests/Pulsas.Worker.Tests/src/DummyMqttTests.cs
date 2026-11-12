using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Pulsas.Worker.ApiService;
using Xunit;

namespace Pulsas.Worker.Tests;

public class DummyMqttTests
{
    [Theory]
    [InlineData("mqtt://localhost:1883", "VNO", false)]
    [InlineData("ws://user:password@localhost/mqtt", "VNO", false)]
    [InlineData("ws://localhost:9001/mqtt#fragment", "VNO", false)]
    [InlineData("ws://localhost:9001/mqtt", "V/+", false)]
    [InlineData("ws://localhost:9001/mqtt", "vno", false)]
    [InlineData("ws://localhost:9001/mqtt", "VNO", true)]
    [InlineData("wss://broker.example/mqtt", "KUN", true)]
    public void Validates_configuration(string endpoint, string region, bool expected) =>
        Assert.Equal(
            expected,
            new DummyMqttOptions { Endpoint = endpoint, Region = region }.IsValid()
        );

    [WorkerBrokerFact]
    public async Task Subscribes_receives_both_topics_and_disconnects_on_shutdown()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var endpoint = Environment.GetEnvironmentVariable("PULSAS_WORKER_MQTT_ENDPOINT")!;
        Assert.True(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri));
        Assert.True(uri.IsLoopback, "This opt-in test is restricted to a local broker.");
        var logger = new CaptureLogger();
        using var consumer = new DummyMqttConsumer(
            Options.Create(new DummyMqttOptions { Endpoint = endpoint, Region = "TST" }),
            logger
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

    private class CaptureLogger : ILogger<DummyMqttConsumer>
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
