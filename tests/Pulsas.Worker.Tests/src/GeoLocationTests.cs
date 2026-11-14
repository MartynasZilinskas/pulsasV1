using Pulsas.Core.Geography;
using Xunit;

namespace Pulsas.Worker.Tests;

public class GeoLocationTests
{
    [Theory]
    [InlineData(-90_000_000, -180_000_000)]
    [InlineData(90_000_000, 180_000_000)]
    [InlineData(54_736_798, 23_655_660)]
    [InlineData(0, 0)]
    [InlineData(0, 30_000_000)]
    [InlineData(45_000_000, 0)]
    public void Valid_coordinates_preserve_exact_microdegrees(int latitude, int longitude)
    {
        var location = GeoLocation
            .FromMicrodegrees(latitude, longitude)
            .Match(
                none: () => throw new InvalidOperationException("Expected a valid location."),
                some: value => value
            );
        Assert.Equal(latitude, location.LatitudeMicrodegrees);
        Assert.Equal(longitude, location.LongitudeMicrodegrees);
    }

    [Theory]
    [InlineData(-90_000_001, 0)]
    [InlineData(90_000_001, 0)]
    [InlineData(0, -180_000_001)]
    [InlineData(0, 180_000_001)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MinValue)]
    [InlineData(0, int.MaxValue)]
    public void Invalid_coordinates_do_not_produce_a_location(int latitude, int longitude) =>
        Assert.True(
            GeoLocation
                .FromMicrodegrees(latitude, longitude)
                .Match(none: () => true, some: _ => false)
        );
}
