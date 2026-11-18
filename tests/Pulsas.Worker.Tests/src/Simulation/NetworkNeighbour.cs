namespace Pulsas.Worker.Tests.Simulation;

// Entries in a node's ordered neighbour list are outgoing, not reciprocal.
public record NetworkNeighbour(string NodeId, int DelayMilliseconds = 0)
{
    public static (NetworkNeighbour FromFirst, NetworkNeighbour FromSecond) Reciprocal(
        string first,
        string second,
        int delayMilliseconds = 0
    ) => (new(second, delayMilliseconds), new(first, delayMilliseconds));
}
