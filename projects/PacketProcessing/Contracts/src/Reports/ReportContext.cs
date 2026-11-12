namespace Pulsas.PacketProcessing.Contracts.Reports;

// Adapter-provided context. Matching a publisher's claim is not authentication.
public record ReportContext(
    string Source,
    string Region,
    string ExpectedObserver,
    DateTimeOffset ArrivedAt
);
