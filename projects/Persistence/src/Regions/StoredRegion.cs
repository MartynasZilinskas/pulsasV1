using NetTopologySuite.Geometries;
using NodaTime;

namespace Pulsas.Persistence.Regions;

public class StoredRegion
{
    public Instant? CreatedAt { get; set; }
    public Instant? ModifiedAt { get; set; }
    public required Guid Id { get; set; }
    public required string Code { get; set; }
    public string? Name { get; set; }
    public required string CountryCode { get; set; }
    public MultiPolygon? Shape { get; set; }
}
