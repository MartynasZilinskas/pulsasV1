namespace Pulsas.PacketProcessing.Contracts.Reports;

public record PacketReport(
    string Observer,
    ReportTimestamp Timestamp,
    string? RawHex,
    int? Rssi,
    double? Snr
);
