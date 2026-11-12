using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pulsas.TrafficGenerator.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task Stalled_connect_times_out_as_failure_but_explicit_stop_and_shutdown_cancel_it()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/mqtt";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        foreach (var action in new[] { "timeout", "stop", "shutdown" })
        {
            using var manager = new TrafficGeneratorManager(
                Options.Create(
                    new TrafficGeneratorOptions { Endpoint = endpoint, ObserverCount = 1 }
                ),
                NullLogger<TrafficGeneratorManager>.Instance
            );
            await manager.StartAsync(deadline.Token);
            // Accept TCP but deliberately never complete the WebSocket handshake.
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            if (action == "timeout")
            {
                while (manager.Status.State == "Running")
                {
                    await Task.Delay(20, deadline.Token);
                }

                Assert.Equal("Failed", manager.Status.State);
                Assert.NotNull(manager.Status.Error);
            }
            else if (action == "stop")
            {
                using var requester = new CancellationTokenSource();
                var stop = manager.StopAsync(requester.Token);
                requester.Cancel();
                Assert.Equal("Stopped", (await stop.WaitAsync(deadline.Token)).State);
            }
            else
            {
                await ((IHostedService)manager).StopAsync(deadline.Token);
                Assert.Equal("Stopped", manager.Status.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    manager.StartAsync(deadline.Token)
                );
            }
            Assert.Equal(0, manager.Status.PublishedPacketReports);
        }
    }

    [Fact]
    public async Task Shutdown_cancels_an_accepted_run_and_rejects_later_starts()
    {
        using var manager = new TrafficGeneratorManager(
            Options.Create(
                new TrafficGeneratorOptions { Endpoint = "not a URI", ObserverCount = 1 }
            ),
            NullLogger<TrafficGeneratorManager>.Instance
        );
        Assert.Equal("Idle", manager.Status.State);
        Assert.Equal("Idle", (await manager.StopAsync(CancellationToken.None)).State);
        await manager.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ((IHostedService)manager).StopAsync(timeout.Token);
        Assert.Contains(manager.Status.State, new[] { "Stopped", "Failed" });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StartAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task Invalid_transport_failure_is_terminal_and_restartable()
    {
        // Direct manager construction bypasses host configuration validation to
        // exercise a real transport setup failure without contacting a broker.
        using var manager = new TrafficGeneratorManager(
            Options.Create(
                new TrafficGeneratorOptions { Endpoint = "not a URI", ObserverCount = 1 }
            ),
            NullLogger<TrafficGeneratorManager>.Instance
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await manager.StartAsync(timeout.Token);
        while (manager.Status.State == "Running")
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.Equal("Failed", manager.Status.State);
        Assert.NotNull(manager.Status.Error);
        Assert.Equal(0, manager.Status.PublishedPacketReports);
        Assert.Equal("Failed", (await manager.StopAsync(timeout.Token)).State);
        var second = await manager.StartAsync(timeout.Token);
        Assert.NotEqual(first.RunId, second.RunId);
        await ((IHostedService)manager).StopAsync(timeout.Token);
    }
}
