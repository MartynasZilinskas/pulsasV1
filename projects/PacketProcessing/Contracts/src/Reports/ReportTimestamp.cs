namespace Pulsas.PacketProcessing.Contracts.Reports;

// A publisher's report-construction claim, never inferred to be radio-hearing time.
public record ReportTimestamp(
    string? Claim,
    ReportTimestampStatus Status,
    DateTimeOffset? Value = null
);

public enum ReportTimestampStatus
{
    Missing,
    Invalid,
    Parsed,
}
