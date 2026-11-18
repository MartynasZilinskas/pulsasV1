using System.Collections.Immutable;
using Pulsas.MeshCore.Nodes;

namespace Pulsas.Worker.Tests.Simulation;

public record NetworkNode(
    string Id,
    string SigningSeedHex,
    string Name,
    NodeRole Role,
    int? LatitudeMicrodegrees = null,
    int? LongitudeMicrodegrees = null,
    ImmutableArray<NetworkNeighbour> Neighbours = default,
    ObserverConfiguration? Observer = null
);
