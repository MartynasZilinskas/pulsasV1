namespace Pulsas.TrafficGenerator;

public interface ITrafficGeneratorManager
{
    TrafficStatus Status { get; }
    Task<TrafficStatus> StartAsync(CancellationToken cancellationToken);
    Task<TrafficStatus> StopAsync(CancellationToken cancellationToken);
}
