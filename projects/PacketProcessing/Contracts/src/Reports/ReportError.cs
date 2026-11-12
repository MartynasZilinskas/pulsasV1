namespace Pulsas.PacketProcessing.Contracts.Reports;

public enum ReportError
{
    UninitializedReport,
    InvalidContext,
    MalformedJson,
    ExpectedObject,
    InvalidObserver,
    ObserverMismatch,
    UnsupportedReport,
    InvalidRawField,
}
