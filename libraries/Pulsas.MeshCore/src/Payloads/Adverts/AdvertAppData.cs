using Pulsas.MeshCore.Nodes;

namespace Pulsas.MeshCore.Payloads.Adverts;

// Coordinates are advertised integer microdegrees, not verified locations.
// Numeric role and feature values are preserved, including unknown values.
public record AdvertAppData(
    NodeRole Role,
    int? LatitudeMicrodegrees = null,
    int? LongitudeMicrodegrees = null,
    ushort? Feature1 = null,
    ushort? Feature2 = null,
    string? Name = null
)
{
    public byte RoleCode => (byte)Role;
}
