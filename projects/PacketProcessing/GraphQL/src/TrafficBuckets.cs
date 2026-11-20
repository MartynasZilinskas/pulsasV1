using NodaTime;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class TrafficBuckets
{
    internal const int MaximumBuckets = 120;
    private static readonly int[] Intervals =
    [
        60,
        300,
        900,
        1800,
        3600,
        7200,
        14400,
        28800,
        43200,
        86400,
    ];

    internal static int SelectInterval(Instant from, Instant to) =>
        Intervals.First(seconds =>
            BinIndex(to - Duration.Epsilon, seconds) - BinIndex(from, seconds) + 1 <= MaximumBuckets
        );

    internal static IEnumerable<(Instant Start, Instant From, Instant To)> Enumerate(
        Instant from,
        Instant to,
        int seconds
    )
    {
        var width = Duration.FromSeconds(seconds);
        var start = Instant.FromUnixTimeSeconds(BinIndex(from, seconds) * seconds);
        while (start < to)
        {
            var end = to - start <= width ? to : start + width;
            yield return (start, start < from ? from : start, end);
            start = end;
        }
    }

    // Integer division rounds toward zero; bins must floor before the Unix epoch too.
    private static long BinIndex(Instant time, int seconds)
    {
        var unix = time.ToUnixTimeSeconds();
        return unix / seconds - (unix % seconds < 0 ? 1 : 0);
    }
}
