using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.PacketProcessing.Core.Ingestion;
using Xunit;

namespace Pulsas.Worker.Tests;

public class ReceptionIdentityTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly PacketReport Report = new(
        new string('a', 64),
        new(At.ToString("O"), ReportTimestampStatus.Parsed, At),
        "050001",
        -72,
        4.25
    );

    [Fact]
    public void Identity_uses_exact_bytes_observer_timestamp_and_nullable_signal()
    {
        byte[] raw = [5, 0, 1];
        var identity = ReceptionIdentity.Create(Report, raw);
        Assert.Equal(32, identity.Length);
        var variants = new[]
        {
            Report with
            {
                Observer = new string('b', 64),
            },
            Report with
            {
                Timestamp = new(
                    At.AddTicks(1).ToString("O"),
                    ReportTimestampStatus.Parsed,
                    At.AddTicks(1)
                ),
            },
            Report with
            {
                Timestamp = new(null, ReportTimestampStatus.Missing),
            },
            Report with
            {
                Timestamp = new("invalid", ReportTimestampStatus.Invalid),
            },
            Report with
            {
                Rssi = -73,
            },
            Report with
            {
                Rssi = null,
            },
            Report with
            {
                Snr = 4.5,
            },
            Report with
            {
                Snr = null,
            },
        };
        Assert.All(variants, report =>
            Assert.NotEqual(identity, ReceptionIdentity.Create(report, raw))
        );
        Assert.NotEqual(identity, ReceptionIdentity.Create(Report, [5, 0, 2]));
        Assert.Equal(
            identity,
            ReceptionIdentity.Create(
                Report with
                {
                    RawHex = "05 00 01",
                    Observer = Report.Observer.ToUpperInvariant(),
                },
                raw
            )
        );

        var offset = At.ToOffset(TimeSpan.FromHours(2));
        Assert.Equal(
            identity,
            ReceptionIdentity.Create(
                Report with
                {
                    Timestamp = new(offset.ToString("O"), ReportTimestampStatus.Parsed, offset),
                },
                raw
            )
        );
        Assert.Equal(
            ReceptionIdentity.Create(Report with { Snr = 0d }, raw),
            ReceptionIdentity.Create(Report with { Snr = -0d }, raw)
        );
        Assert.NotEqual(
            ReceptionIdentity.Create(
                Report with
                {
                    Timestamp = new("bad", ReportTimestampStatus.Invalid),
                },
                raw
            ),
            ReceptionIdentity.Create(
                Report with
                {
                    Timestamp = new("worse", ReportTimestampStatus.Invalid),
                },
                raw
            )
        );
    }
}
