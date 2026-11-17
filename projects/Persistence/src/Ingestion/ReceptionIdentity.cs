using System.Security.Cryptography;
using System.Text;
using Pulsas.PacketProcessing.Contracts.Reports;

namespace Pulsas.Persistence.Ingestion;

public static class ReceptionIdentity
{
    public static byte[] Create(PacketReport report, byte[] raw)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        writer.Write(Convert.FromHexString(report.Observer));
        writer.Write(raw.Length);
        writer.Write(raw);
        writer.Write((int)report.Timestamp.Status);
        switch (report.Timestamp.Status)
        {
            case ReportTimestampStatus.Parsed:
                writer.Write(report.Timestamp.Value!.Value.UtcTicks);
                break;
            case ReportTimestampStatus.Invalid:
                writer.Write(report.Timestamp.Claim!);
                break;
        }
        writer.Write(report.Rssi.HasValue);
        if (report.Rssi is { } rssi)
        {
            writer.Write(rssi);
        }
        writer.Write(report.Snr.HasValue);
        if (report.Snr is { } snr)
        {
            writer.Write(snr == 0 ? 0d : snr);
        }
        writer.Flush();
        return SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }
}
