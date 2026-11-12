using System.Globalization;
using System.Text.Json;
using Funcky.Monads;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;

namespace Pulsas.PacketProcessing.Core.Reports;

internal static class ObserverReportDecoder
{
    public static Either<ReportError, PacketReport> Decode(IPacketProcessingManager.Input input)
    {
        if (input.OriginalReport.IsDefault)
        {
            return Either<ReportError, PacketReport>.Left(ReportError.UninitializedReport);
        }
        var context = input.Context;
        if (
            string.IsNullOrWhiteSpace(context.Source)
            || string.IsNullOrWhiteSpace(context.Region)
            || !IsPublicKey(context.ExpectedObserver)
        )
        {
            return Either<ReportError, PacketReport>.Left(ReportError.InvalidContext);
        }

        try
        {
            using var document = JsonDocument.Parse(input.OriginalReport.AsMemory());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Either<ReportError, PacketReport>.Left(ReportError.ExpectedObject);
            }
            var observer = Text(root, "origin_id");
            if (!IsPublicKey(observer))
            {
                return Either<ReportError, PacketReport>.Left(ReportError.InvalidObserver);
            }
            if (
                !string.Equals(
                    observer,
                    context.ExpectedObserver,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Either<ReportError, PacketReport>.Left(ReportError.ObserverMismatch);
            }
            if (Text(root, "type") != "PACKET" || Text(root, "direction") != "rx")
            {
                return Either<ReportError, PacketReport>.Left(ReportError.UnsupportedReport);
            }
            if (
                root.TryGetProperty("raw", out var raw)
                && raw.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            )
            {
                return Either<ReportError, PacketReport>.Left(ReportError.InvalidRawField);
            }

            return new PacketReport(
                observer!.ToLowerInvariant(),
                Timestamp(root),
                Text(root, "raw"),
                int.TryParse(
                    NumericText(root, "RSSI"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var rssi
                )
                    ? rssi
                    : null,
                double.TryParse(
                    NumericText(root, "SNR"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var snr
                ) && double.IsFinite(snr)
                    ? snr
                    : null
            );
        }
        catch (JsonException)
        {
            return Either<ReportError, PacketReport>.Left(ReportError.MalformedJson);
        }
    }

    private static ReportTimestamp Timestamp(JsonElement root)
    {
        if (
            !root.TryGetProperty("timestamp", out var value)
            || value.ValueKind == JsonValueKind.Null
        )
        {
            return new(null, ReportTimestampStatus.Missing);
        }
        var claim =
            value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        // Require ISO date/time with an explicit offset; never assume the host timezone.
        var formats = new[] { "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" };
        return
            DateTimeOffset.TryParseExact(
                claim,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
            && (
                claim.EndsWith('Z')
                || (claim.Length >= 6 && claim[^3] == ':' && claim[^6] is '+' or '-')
            )
            ? new(claim, ReportTimestampStatus.Parsed, parsed)
            : new(claim, ReportTimestampStatus.Invalid);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NumericText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static bool IsPublicKey(string? value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
