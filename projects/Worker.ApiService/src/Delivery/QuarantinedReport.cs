namespace Pulsas.Worker.ApiService.Delivery;

public record QuarantinedReport(
    string SourceQueue,
    long MessageId,
    int Attempts,
    string Reason,
    string OriginalEnvelope,
    DateTimeOffset QuarantinedAt
);
