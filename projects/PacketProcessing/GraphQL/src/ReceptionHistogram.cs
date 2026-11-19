using System.Collections.Immutable;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.GraphQL.ReceptionBrowserCursor;

namespace Pulsas.PacketProcessing.GraphQL;

internal static class ReceptionHistogram
{
    private const string Collection = "reception-histogram-v1";
    private static readonly int[] Intervals = [900, 1800, 3600, 7200, 14400, 28800, 43200, 86400];

    internal static async Task<MeshPacketReceptionHistogram> ReadAsync(
        MeshPacketReceptionFilterInput? input,
        string? refreshCursor,
        Instant now,
        ReceptionBrowserCursor cursors,
        IReceptionBrowserManager reads,
        CancellationToken cancellationToken
    )
    {
        var requested = ReceptionBrowserFilter.FromInput(input);
        var cursor = refreshCursor is null
            ? null
            : cursors.Decode(
                refreshCursor,
                Collection,
                input is null ? null : Fingerprint(requested.EncodeScope())
            );
        var effective = cursor is null ? requested : cursor.Filter ?? throw InvalidCursor();
        var filter = effective.ToFilter();
        var to = filter.ArrivedTo is { } upper && upper < now ? upper : now;
        var cutoff = now - Duration.FromDays(30);
        // An upper bound older than the histogram's lookback cannot contribute any rows.
        if (to <= cutoff)
        {
            return new(to, to, Intervals[0], true, null, []);
        }
        var from = filter.ArrivedFrom ?? to - Duration.FromDays(1);
        var align = filter.ArrivedFrom is null || from < cutoff;
        if (from < cutoff)
        {
            from = cutoff;
        }
        if (from >= to)
        {
            return new(to, to, Intervals[0], true, null, []);
        }
        var interval = Intervals.First(seconds =>
            BucketCount(align ? Ceil(from, seconds) : from, to, seconds) <= 120
        );
        from = align ? Ceil(from, interval) : from;
        if (from >= to)
        {
            return new(to, to, interval, true, null, []);
        }

        Instant? tail = cursor is null ? null : Parse(cursor.Time);
        var isSnapshot = tail is null || tail < from || tail >= to || cursor?.Interval != interval;
        var queryFrom = isSnapshot ? from : tail!.Value;
        var countRows = await reads.ReadHistogramAsync(
            filter,
            queryFrom,
            to,
            interval,
            cancellationToken
        );
        var byBucket = countRows.ToLookup(row => row.Bucket);
        var buckets = ImmutableArray.CreateBuilder<MeshPacketReceptionHistogramBucket>();
        var width = Duration.FromSeconds(interval);
        for (var start = Floor(queryFrom, interval); start < to; start += width)
        {
            var end = start + width;
            var counts = byBucket[start]
                .OrderBy(row => row.Type)
                .Select(row => new MeshPacketReceptionTypeCount(
                    (LegacyPacketType?)row.Type,
                    row.Count
                ))
                .ToImmutableArray();
            buckets.Add(
                new(
                    start < queryFrom ? queryFrom : start,
                    end > to ? to : end,
                    counts.Sum(row => row.Count),
                    counts
                )
            );
        }
        var nextTail = Floor(to - Duration.Epsilon, interval) - width;
        var next =
            nextTail < from
                ? null
                : cursors.Encode(
                    new(
                        Collection,
                        cursor?.Scope ?? Fingerprint(effective.EncodeScope()),
                        Guid.Empty,
                        Time: Format(nextTail),
                        Interval: interval,
                        Filter: effective
                    )
                );
        return new(from, to, interval, isSnapshot, next, buckets.ToImmutable());
    }

    private static long BucketCount(Instant from, Instant to, int seconds) =>
        from >= to
            ? 0
            : (
                Floor(to - Duration.Epsilon, seconds).ToUnixTimeSeconds()
                - Floor(from, seconds).ToUnixTimeSeconds()
            ) / seconds
                + 1;

    private static Instant Ceil(Instant time, int seconds)
    {
        var floor = Floor(time, seconds);
        return floor == time ? time : floor + Duration.FromSeconds(seconds);
    }

    private static Instant Floor(Instant time, int seconds)
    {
        var unix = time.ToUnixTimeSeconds();
        return Instant.FromUnixTimeSeconds(unix - ((unix % seconds + seconds) % seconds));
    }
}
