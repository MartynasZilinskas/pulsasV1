namespace Pulsas.Worker.ApiService.Delivery;

public record ReportDeliveryOptions
{
    // Fixed production topology; explicit overrides isolate tests in a retained database.
    public string IngestionQueue { get; init; } = ReportQueues.Ingestion;
    public string ContentQueue { get; init; } = ReportQueues.Content;
    public string QuarantineQueue { get; init; } = ReportQueues.Quarantine;
    public int MaximumAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 5;
    public int PollDelayMilliseconds { get; set; } = 250;
    public int ContentConcurrency { get; set; } = 4;

    public bool IsValid() =>
        MaximumAttempts > 0
        && RetryDelaySeconds >= 0
        && PollDelayMilliseconds > 0
        && ContentConcurrency > 0
        && new[] { IngestionQueue, ContentQueue, QuarantineQueue }
            .Distinct(StringComparer.Ordinal)
            .Count() == 3
        && new[] { IngestionQueue, ContentQueue, QuarantineQueue }.All(name =>
            name is { Length: > 0 and <= 48 }
            && name.All(character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '_'
            )
        );
}
