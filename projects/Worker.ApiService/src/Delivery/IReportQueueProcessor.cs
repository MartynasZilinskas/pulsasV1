namespace Pulsas.Worker.ApiService.Delivery;

public interface IReportQueueProcessor
{
    Task<bool> ProcessNextAsync(string queueName, CancellationToken cancellationToken);
}
