namespace Pulsas.TrafficGenerator;

// Counts are completed publish calls, not Worker ingestion or database commits.
public record TrafficStatus(
    string State,
    Guid? RunId = null,
    int CompletedRounds = 0,
    long PublishedPacketReports = 0,
    long PublishedStatusReports = 0,
    string? Error = null,
    bool Connected = false
);
