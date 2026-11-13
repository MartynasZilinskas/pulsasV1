using System.Collections.Immutable;

namespace Pulsas.Worker.ApiService.Configuration;

public record ConfiguredRegionGroup(
    string Id,
    string Name,
    ImmutableArray<string> Regions,
    ConfiguredCenter? Center,
    double? Zoom
);
