namespace Pulsas.PacketProcessing.GraphQL;

public record ObserverNotFoundError : IObserverActivitySummaryResult
{
    public string Message => "No observer has that ID.";
}
