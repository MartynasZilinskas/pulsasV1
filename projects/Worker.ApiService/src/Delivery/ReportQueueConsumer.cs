using Microsoft.Extensions.Options;

namespace Pulsas.Worker.ApiService.Delivery;

public class ReportQueueConsumer(
    IServiceScopeFactory scopes,
    IOptions<ReportDeliveryOptions> options,
    ILogger<ReportQueueConsumer> logger
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            Enumerable
                .Range(0, options.Value.ContentConcurrency)
                .Select(_ => ConsumeAsync(options.Value.ContentQueue, stoppingToken))
                .Prepend(ConsumeAsync(options.Value.IngestionQueue, stoppingToken))
        );

    private async Task ConsumeAsync(string queueName, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var handled = false;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    handled = await scope
                        .ServiceProvider.GetRequiredService<IReportQueueProcessor>()
                        .ProcessNextAsync(queueName, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "Queue {Queue} delivery transaction failed: {Reason}",
                        queueName,
                        exception.GetType().Name
                    );
                }
                if (!handled)
                {
                    await Task.Delay(options.Value.PollDelayMilliseconds, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
