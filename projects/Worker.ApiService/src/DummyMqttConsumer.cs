using System.Buffers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Pulsas.Worker.ApiService.Delivery;

namespace Pulsas.Worker.ApiService;

// Development-only transport; acknowledgment follows durable report acceptance.
public class DummyMqttConsumer(
    IOptions<DummyMqttOptions> options,
    ILogger<DummyMqttConsumer> logger,
    IReportReceiver reports
) : BackgroundService, IHealthCheck
{
    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private volatile bool subscribed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task OnMessage(MqttApplicationMessageReceivedEventArgs args) =>
            OnMessageReceivedAsync(args, stoppingToken);
        client.ApplicationMessageReceivedAsync += OnMessage;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken
                    );
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var connection = await client.ConnectAsync(
                        new MqttClientOptionsBuilder()
                            .WithClientId($"pulsas-worker-{Guid.NewGuid():N}"[..23])
                            .WithProtocolVersion(MqttProtocolVersion.V311)
                            .WithCleanSession()
                            .WithWebSocketServer(server => server.WithUri(options.Value.Endpoint))
                            .Build(),
                        timeout.Token
                    );
                    if ((int)connection.ResultCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"MQTT connection refused: {connection.ResultCode}."
                        );
                    }

                    var statusTopic = $"meshcore/{options.Value.Region}/+/status";
                    var packetsTopic = $"meshcore/{options.Value.Region}/+/packets";
                    var subscription = await client.SubscribeAsync(
                        new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter(filter =>
                                filter
                                    .WithTopic(statusTopic)
                                    .WithQualityOfServiceLevel(
                                        MqttQualityOfServiceLevel.AtLeastOnce
                                    )
                            )
                            .WithTopicFilter(filter =>
                                filter
                                    .WithTopic(packetsTopic)
                                    .WithQualityOfServiceLevel(
                                        MqttQualityOfServiceLevel.AtLeastOnce
                                    )
                            )
                            .Build(),
                        timeout.Token
                    );
                    if (
                        subscription.Items.Count != 2
                        || subscription.Items.Any(item => (int)item.ResultCode > 2)
                    )
                    {
                        throw new InvalidOperationException(
                            "MQTT broker rejected dummy consumer subscriptions."
                        );
                    }

                    subscribed = true;
                    logger.LogInformation(
                        "Dummy MQTT consumer subscribed to {StatusTopic} and {PacketsTopic}",
                        statusTopic,
                        packetsTopic
                    );
                    while (client.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                    logger.LogWarning("Dummy MQTT consumer disconnected; retrying.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Dummy MQTT consumer connection/subscription failed; retrying."
                    );
                }
                finally
                {
                    subscribed = false;
                    if (client.IsConnected)
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try
                        {
                            await client.DisconnectAsync(
                                new MqttClientDisconnectOptions(),
                                cleanup.Token
                            );
                        }
                        catch (Exception exception)
                        {
                            logger.LogWarning(exception, "Dummy MQTT consumer disconnect failed.");
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            client.ApplicationMessageReceivedAsync -= OnMessage;
        }
    }

    private async Task OnMessageReceivedAsync(
        MqttApplicationMessageReceivedEventArgs args,
        CancellationToken cancellationToken
    )
    {
        var arrivedAt = DateTimeOffset.UtcNow;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Dummy MQTT received {Topic}: {PayloadBytes} bytes, QoS {Qos}, retained {Retained}",
                args.ApplicationMessage.Topic,
                args.ApplicationMessage.Payload.Length,
                args.ApplicationMessage.QualityOfServiceLevel,
                args.ApplicationMessage.Retain
            );
        }
        if (args.ApplicationMessage.Topic.EndsWith("/packets", StringComparison.Ordinal))
        {
            try
            {
                await reports.ReceiveReportAsync(
                    new(
                        Guid.NewGuid(),
                        [.. args.ApplicationMessage.Payload.ToArray()],
                        args.ApplicationMessage.Topic,
                        options.Value.Endpoint,
                        arrivedAt
                    ),
                    cancellationToken
                );
            }
            catch
            {
                args.AutoAcknowledge = false;
                throw;
            }
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            subscribed && client.IsConnected
                ? HealthCheckResult.Healthy(
                    "MQTT subscriptions are active; packet reports are durably queued before acknowledgment."
                )
                : HealthCheckResult.Unhealthy("Dummy MQTT consumer is not subscribed.")
        );

    public override void Dispose()
    {
        base.Dispose();
        client.Dispose();
    }
}
