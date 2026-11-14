using Funcky.Extensions;
using Funcky.Monads;

namespace Pulsas.Core.Geography;

public record GeoLocation
{
    public int LatitudeMicrodegrees { get; }
    public int LongitudeMicrodegrees { get; }

    private GeoLocation(int latitudeMicrodegrees, int longitudeMicrodegrees)
    {
        LatitudeMicrodegrees = latitudeMicrodegrees;
        LongitudeMicrodegrees = longitudeMicrodegrees;
    }

    public static Option<GeoLocation> FromMicrodegrees(
        int latitudeMicrodegrees,
        int longitudeMicrodegrees
    ) =>
        (
            latitudeMicrodegrees is >= -90_000_000 and <= 90_000_000
            && longitudeMicrodegrees is >= -180_000_000 and <= 180_000_000
        ).Then(() => new GeoLocation(latitudeMicrodegrees, longitudeMicrodegrees));
}
