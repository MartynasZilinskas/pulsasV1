namespace Pulsas.Worker.ApiService.Configuration;

public record ConfiguredRegion(
    string Id,
    string Name,
    string Country,
    ConfiguredCenter Center,
    string? BorderFile,
    double? Zoom
);
