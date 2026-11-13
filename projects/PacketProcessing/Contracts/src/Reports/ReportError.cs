namespace Pulsas.PacketProcessing.Contracts.Reports;

public enum ReportError
{
    UninitializedReport = 0,
    InvalidContext = 1,
    MalformedJson = 2,
    ExpectedObject = 3,
    InvalidObserver = 4,
    ObserverMismatch = 5,
    UnsupportedReport = 6,
    InvalidRawField = 7,
}
