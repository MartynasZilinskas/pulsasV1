using System.Globalization;
using NodaTime;
using Pulsas.Persistence;
using Xunit;

namespace Pulsas.Worker.Tests;

public class GuidV7Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1794477600123)]
    public void Uses_supplied_clock_for_uuid_timestamp(long milliseconds)
    {
        var clock = new FixedClock(Instant.FromUnixTimeMilliseconds(milliseconds));
        var id = GuidV7.Create(clock).ToString("N");
        Assert.Equal(
            milliseconds,
            long.Parse(id[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        );
        Assert.Equal('7', id[12]);
        Assert.Contains(id[16], "89ab");
    }

    private class FixedClock(Instant instant) : IClock
    {
        public Instant GetCurrentInstant() => instant;
    }
}
