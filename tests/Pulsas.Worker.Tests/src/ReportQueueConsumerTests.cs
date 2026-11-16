using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pulsas.Worker.ApiService.Delivery;
using Xunit;

namespace Pulsas.Worker.Tests;

public class ReportQueueConsumerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task Runs_one_ingestion_loop_and_configured_content_pool_with_independent_scopes(
        int concurrency
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var probe = new Probe();
        var options = new ReportDeliveryOptions { ContentConcurrency = concurrency };
        await using var services = new ServiceCollection()
            .AddSingleton(probe)
            .AddScoped<IReportQueueProcessor, BlockingProcessor>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var consumer = new ReportQueueConsumer(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReportQueueConsumer>.Instance
        );
        await consumer.StartAsync(token);
        try
        {
            var calls = new List<Call>();
            for (var i = 0; i < concurrency + 1; i++)
            {
                calls.Add(await probe.Calls.Reader.ReadAsync(token));
            }
            Assert.Single(calls, call => call.Queue == options.IngestionQueue);
            Assert.Equal(concurrency, calls.Count(call => call.Queue == options.ContentQueue));
            Assert.Equal(concurrency + 1, calls.Select(call => call.Scope).Distinct().Count());
            Assert.Equal(concurrency + 1, probe.Active.Count);
            Assert.False(probe.Calls.Reader.TryRead(out _));

            // A completed iteration must dispose its scope and obtain a new one, not grow the pool.
            var completed = calls.First(call => call.Queue == options.ContentQueue);
            completed.Release.SetResult();
            var next = await probe.Calls.Reader.ReadAsync(token);
            Assert.Equal(options.ContentQueue, next.Queue);
            Assert.DoesNotContain(next.Scope, calls.Select(call => call.Scope));
            Assert.Contains(completed.Scope, probe.Disposed);
            Assert.Equal(concurrency + 1, probe.Active.Count);
            Assert.False(probe.Calls.Reader.TryRead(out _));
        }
        finally
        {
            await consumer.StopAsync(token);
        }
        Assert.Empty(probe.Active);
        Assert.Equal(concurrency + 2, probe.Disposed.Count);
    }

    private record Call(Guid Scope, string Queue, TaskCompletionSource Release);

    private class Probe
    {
        public Channel<Call> Calls { get; } = Channel.CreateUnbounded<Call>();
        public ConcurrentDictionary<Guid, byte> Active { get; } = new();
        public ConcurrentBag<Guid> Disposed { get; } = [];
    }

    private class BlockingProcessor(Probe probe) : IReportQueueProcessor, IAsyncDisposable
    {
        private readonly Guid scope = Guid.NewGuid();

        public async Task<bool> ProcessNextAsync(
            string queueName,
            CancellationToken cancellationToken
        )
        {
            Assert.True(probe.Active.TryAdd(scope, 0));
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await probe.Calls.Writer.WriteAsync(new(scope, queueName, release), cancellationToken);
            await release.Task.WaitAsync(cancellationToken);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            Assert.True(probe.Active.TryRemove(scope, out _));
            probe.Disposed.Add(scope);
            return ValueTask.CompletedTask;
        }
    }
}
