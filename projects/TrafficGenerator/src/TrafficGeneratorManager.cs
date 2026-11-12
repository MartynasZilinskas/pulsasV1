using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Pulsas.MeshCore.Cryptography;

namespace Pulsas.TrafficGenerator;

public class TrafficGeneratorManager(
    IOptions<TrafficGeneratorOptions> options,
    ILogger<TrafficGeneratorManager> logger
) : ITrafficGeneratorManager, IHostedService, IDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly IMqttClient client = new MqttClientFactory().CreateMqttClient();
    private readonly object statusLock = new();
    private TrafficStatus status = new("Idle");
    private CancellationTokenSource? runCancellation;
    private Task run = Task.CompletedTask;
    private bool shuttingDown;

    public TrafficStatus Status
    {
        get
        {
            lock (statusLock)
            {
                return status with { Connected = client.IsConnected };
            }
        }
    }

    public async Task<TrafficStatus> StartAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (shuttingDown)
            {
                throw new InvalidOperationException("Traffic generator is shutting down.");
            }

            if (!run.IsCompleted)
            {
                return Status;
            }

            runCancellation?.Dispose();
            runCancellation = new();
            var token = runCancellation.Token;
            var initial = new TrafficStatus("Running", Guid.NewGuid());
            lock (statusLock)
            {
                status = initial;
            }

            run = Task.Run(() => PublishAsync(token), CancellationToken.None);
            return Status;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task<TrafficStatus> StopAsync(CancellationToken cancellationToken)
    {
        var pending = await CancelAsync(false, cancellationToken);
        // Once accepted, a disconnected HTTP caller must not abandon cleanup.
        await pending;
        return Status;
    }

    private async Task<Task> CancelAsync(bool shutdown, CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            shuttingDown |= shutdown;
            if (!run.IsCompleted)
            {
                lock (statusLock)
                {
                    if (status.State == "Running")
                    {
                        status = status with { State = "Stopping" };
                    }
                }

                runCancellation!.Cancel();
            }
            return run;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (statusLock)
            {
                status = status with { State = "Failed", Error = exception.GetType().Name };
            }

            logger.LogError(exception, "Initial MQTT connection failed; Start traffic can retry.");
        }
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        var pending = await CancelAsync(true, CancellationToken.None);
        await pending.WaitAsync(cancellationToken);
        if (client.IsConnected)
        {
            using var timeout = OperationTimeout(cancellationToken);
            await client.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token);
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        var observers = System.Collections.Immutable.ImmutableArray<ObserverIdentity>.Empty;
        var terminalState = "Completed";
        string? error = null;
        try
        {
            observers = ObserverReports.CreateObservers(config.ObserverCount, cancellationToken);
            using var sender = AdvertSigningKey.Generate();
            if (!client.IsConnected)
            {
                await ConnectAsync(cancellationToken);
            }

            foreach (var observer in observers)
            {
                await PublishReportAsync(
                    observer,
                    "status",
                    ObserverReports.Status(observer, "online", DateTimeOffset.UtcNow),
                    cancellationToken
                );
            }

            for (var round = 0; round < config.Rounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var at = DateTimeOffset.UtcNow;
                var raw = ObserverReports.CreateAdvert(sender, round, at);
                foreach (var observer in observers)
                {
                    await PublishReportAsync(
                        observer,
                        "packets",
                        ObserverReports.Packet(observer, raw, at),
                        cancellationToken
                    );
                }

                lock (statusLock)
                {
                    status = status with { CompletedRounds = round + 1 };
                }

                if (round < config.Rounds - 1)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(config.IntervalMilliseconds),
                        cancellationToken
                    );
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            terminalState = "Stopped";
        }
        catch (Exception exception)
        {
            terminalState = "Failed";
            error = exception.GetType().Name;
            logger.LogError(exception, "Traffic generation failed ({ErrorType}).", error);
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (client.IsConnected)
                {
                    foreach (var observer in observers)
                    {
                        await PublishReportAsync(
                            observer,
                            "status",
                            ObserverReports.Status(observer, "offline", DateTimeOffset.UtcNow),
                            cleanup.Token
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                terminalState = "Failed";
                error ??= exception.GetType().Name;
                logger.LogWarning(
                    exception,
                    "Traffic cleanup failed ({ErrorType}).",
                    exception.GetType().Name
                );
            }
            lock (statusLock)
            {
                status = status with { State = terminalState, Error = error };
            }
        }

        async Task PublishReportAsync(
            ObserverIdentity observer,
            string kind,
            byte[] payload,
            CancellationToken token
        )
        {
            using var timeout = OperationTimeout(token);
            var result = await client.PublishAsync(
                new MqttApplicationMessageBuilder()
                    .WithTopic($"meshcore/{config.Region}/{observer.PublicKey}/{kind}")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)config.Qos)
                    .WithRetainFlag(false)
                    .Build(),
                timeout.Token
            );
            if ((int)result.ReasonCode >= 128)
            {
                throw new InvalidOperationException($"MQTT publish failed: {result.ReasonCode}.");
            }

            lock (statusLock)
            {
                status =
                    kind == "packets"
                        ? status with
                        {
                            PublishedPacketReports = status.PublishedPacketReports + 1,
                        }
                        : status with
                        {
                            PublishedStatusReports = status.PublishedStatusReports + 1,
                        };
            }
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using var timeout = OperationTimeout(cancellationToken);
        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId($"pulsas-{Guid.NewGuid():N}"[..23])
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithWebSocketServer(server => server.WithUri(options.Value.Endpoint))
            .Build();
        var result = await client.ConnectAsync(clientOptions, timeout.Token);
        if ((int)result.ResultCode != 0)
        {
            throw new InvalidOperationException($"MQTT connection refused: {result.ResultCode}.");
        }
    }

    private static CancellationTokenSource OperationTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }

    public void Dispose()
    {
        client.Dispose();
        runCancellation?.Dispose();
        lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }
}
