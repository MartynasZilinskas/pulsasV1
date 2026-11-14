using System.Text;
using System.Text.Json;
using Funcky.Monads;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Contracts.Reports;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.TrafficGenerator;
using Xunit;
using Xunit.Sdk;

namespace Pulsas.PacketProcessing.Tests;

public class PacketProcessingTests
{
    private static readonly DateTimeOffset Arrival = new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero);
    private static readonly string Observer = new('a', 64);
    private readonly PacketProcessingManager manager = new();

    [Fact]
    public void Processes_real_generator_report_without_conflating_observer_and_sender()
    {
        using var sender = AdvertSigningKey.Generate();
        var reportedAt = Arrival.AddMinutes(-2);
        var raw = ObserverReports.CreateAdvert(sender, 1, reportedAt);
        var bytes = ObserverReports.Packet(new(Observer, "observer"), raw, reportedAt);
        var input = Input(bytes);
        var result = Success(manager.Process(input));
        var packet = Assert.IsType<PacketOutcome.Decoded>(result.Packet);
        var advert = Assert.IsType<PayloadInterpretation.Advert>(packet.Interpretation);
        Assert.Equal(Observer, result.Report.Observer);
        Assert.NotEqual(Observer, Convert.ToHexStringLower(advert.Payload.PublicKey.AsSpan()));
        Assert.Equal(AdvertSignatureStatus.Verified, advert.Signature);
        Assert.Equal(
            "synthetic-1",
            advert.AppData.Match<object?>(
                left: error => error,
                right: data => data.Match<string?>(none: () => null, some: claims => claims.Name)
            )
        );
        Assert.Equal(reportedAt, result.Report.Timestamp.Value);
        Assert.Equal(Arrival, result.Input.Context.ArrivedAt);
        Assert.Equal(raw, result.Report.RawHex);
        Assert.Equal(bytes, result.Input.OriginalReport.ToArray());
    }

    private static IPacketProcessingManager.Input Input(byte[] bytes) =>
        new([.. bytes], new("test", "KUN", Observer, Arrival));

    [Fact]
    public void Rejects_uninitialized_input_and_invalid_adapter_context()
    {
        Assert.Equal(
            ReportError.UninitializedReport,
            manager
                .Process(Input([]) with { OriginalReport = default })
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected rejection")
                )
        );
        Assert.Equal(
            ReportError.InvalidContext,
            manager
                .Process(Input([]) with { Context = new("", "KUN", Observer, Arrival) })
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected rejection")
                )
        );
    }

    [Fact]
    public void Observer_case_and_transport_region_do_not_freeze_generator_defaults()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin_id = Observer.ToUpperInvariant(),
                type = "PACKET",
                direction = "rx",
                raw = "050001",
                RSSI = -72,
                SNR = 4.25,
            }
        );
        var result = Success(
            manager.Process(
                Input(bytes) with
                {
                    Context = new("import", "OTHER", Observer, Arrival),
                }
            )
        );
        Assert.Equal(Observer, result.Report.Observer);
        Assert.Equal("OTHER", result.Input.Context.Region);
        Assert.Equal(-72, result.Report.Rssi);
        Assert.Equal(4.25, result.Report.Snr);
    }

    private static IPacketProcessingManager.Result Success(
        Either<ReportError, IPacketProcessingManager.Result> value
    ) =>
        value.Match(
            left: error => throw new XunitException($"Unexpected rejection: {error}"),
            right: result => result
        );

    [Theory]
    [InlineData("{", ReportError.MalformedJson)]
    [InlineData("[]", ReportError.ExpectedObject)]
    [InlineData("{}", ReportError.InvalidObserver)]
    public void Rejects_bad_envelopes(string json, ReportError expected) =>
        Assert.Equal(
            expected,
            manager
                .Process(Input(Encoding.UTF8.GetBytes(json)))
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected rejection")
                )
        );

    [Theory]
    [InlineData("different", "rx", ReportError.ObserverMismatch)]
    [InlineData("same", "tx", ReportError.UnsupportedReport)]
    public void Rejects_mismatch_and_non_rx(string observer, string direction, ReportError expected)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin_id = observer == "same" ? Observer : new string('b', 64),
                type = "PACKET",
                direction,
                raw = "050001",
            }
        );
        Assert.Equal(
            expected,
            manager
                .Process(Input(bytes))
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected rejection")
                )
        );
    }

    [Theory]
    [InlineData(null, typeof(PacketOutcome.Absent))]
    [InlineData("", typeof(PacketOutcome.Absent))]
    [InlineData("not-hex", typeof(PacketOutcome.InvalidHex))]
    [InlineData("01", typeof(PacketOutcome.Malformed))]
    [InlineData("05 00 01", typeof(PacketOutcome.Decoded))]
    public void Preserves_valid_reports_with_unusable_or_opaque_packets(string? raw, Type expected)
    {
        var result = Process(raw);
        Assert.IsType(expected, result.Packet);
        Assert.Equal(raw, result.Report.RawHex);
        if (result.Packet is PacketOutcome.Decoded packet)
        {
            Assert.IsType<PayloadInterpretation.Opaque>(packet.Interpretation);
        }
    }

    [Theory]
    [InlineData(null, ReportTimestampStatus.Missing)]
    [InlineData("bad", ReportTimestampStatus.Invalid)]
    [InlineData("2026-11-12T01:00:00", ReportTimestampStatus.Invalid)]
    [InlineData("2026-11-12T01:00:00Z", ReportTimestampStatus.Parsed)]
    [InlineData("2026-11-12T03:00:00.1234567+02:00", ReportTimestampStatus.Parsed)]
    public void Timestamp_claims_never_default_to_local_time(
        string? timestamp,
        ReportTimestampStatus status
    )
    {
        var result = Process("050001", timestamp);
        Assert.Equal(status, result.Report.Timestamp.Status);
        Assert.Equal(timestamp, result.Report.Timestamp.Claim);
        Assert.Equal(
            status == ReportTimestampStatus.Parsed,
            result.Report.Timestamp.Value.HasValue
        );
    }

    [Fact]
    public void Invalid_signature_keeps_advert_and_claims()
    {
        using var sender = AdvertSigningKey.Generate();
        var raw = Convert.FromHexString(ObserverReports.CreateAdvert(sender, 1, Arrival));
        raw[38] ^= 1;
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process(Convert.ToHexString(raw)).Packet);
        var advert = Assert.IsType<PayloadInterpretation.Advert>(packet.Interpretation);
        Assert.Equal(AdvertSignatureStatus.Invalid, advert.Signature);
        Assert.Equal(
            "synthetic-1",
            advert.AppData.Match<object?>(
                left: error => error,
                right: data => data.Match<string?>(none: () => null, some: claims => claims.Name)
            )
        );
        Assert.Equal(raw, packet.Raw.ToArray());
    }

    [Theory]
    [InlineData("82ff", AdvertAppDataDecodeErrorKind.Malformed, true)]
    [InlineData("02ff", AdvertAppDataDecodeErrorKind.Unsupported, true)]
    [InlineData("12", AdvertAppDataDecodeErrorKind.Malformed, false)]
    public void App_data_failure_keeps_partial_claims_signature_and_original_bytes(
        string hex,
        AdvertAppDataDecodeErrorKind kind,
        bool hasPartialData
    )
    {
        using var sender = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(sender, 1, Convert.FromHexString(hex));
        var payload = Assert.IsType<byte[]>(
            AdvertCodec.Encode(advert).Match<object>(left: error => error, right: bytes => bytes)
        );
        var raw = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, payload)
        );
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process(Convert.ToHexString(raw)).Packet);
        var interpretation = Assert.IsType<PayloadInterpretation.Advert>(packet.Interpretation);
        Assert.Equal(AdvertSignatureStatus.Verified, interpretation.Signature);
        Assert.Equal(
            new AdvertAppDataDecodeError(kind, hasPartialData ? new(NodeRole.Repeater) : null),
            interpretation.AppData.Match<object>(left: error => error, right: data => data)
        );
        Assert.Equal(raw, packet.Raw.ToArray());
    }

    [Fact]
    public void Empty_app_data_remains_successful_absence_with_verified_signature()
    {
        using var sender = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(sender, 1, []);
        var payload = Assert.IsType<byte[]>(
            AdvertCodec.Encode(advert).Match<object>(left: error => error, right: bytes => bytes)
        );
        var raw = PacketCodec.Encode(
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, payload)
        );
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process(Convert.ToHexString(raw)).Packet);
        var interpretation = Assert.IsType<PayloadInterpretation.Advert>(packet.Interpretation);
        Assert.Equal(AdvertSignatureStatus.Verified, interpretation.Signature);
        Assert.Equal(
            Option<AdvertAppData>.None,
            interpretation.AppData.Match<object>(left: error => error, right: data => data)
        );
    }

    [Fact]
    public void Future_version_is_preserved_without_current_payload_interpretation()
    {
        using var sender = AdvertSigningKey.Generate();
        var raw = Convert.FromHexString(ObserverReports.CreateAdvert(sender, 1, Arrival));
        raw[0] |= 0x40;
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process(Convert.ToHexString(raw)).Packet);
        Assert.IsType<PayloadInterpretation.UnsupportedVersion>(packet.Interpretation);
        Assert.Equal(raw, packet.Raw.ToArray());
    }

    [Fact]
    public void Width_four_is_preserved_without_path_qualification()
    {
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process("05C10102030401").Packet);
        Assert.Equal(4, packet.Packet.PathWidth);
        Assert.False(packet.Packet.IsSupportedPathWidth);
    }

    [Fact]
    public void Malformed_advert_is_an_interpretation_not_report_rejection()
    {
        var packet = Assert.IsType<PacketOutcome.Decoded>(Process("110001").Packet);
        Assert.IsType<PayloadInterpretation.MalformedAdvert>(packet.Interpretation);
    }

    [Fact]
    public void Numeric_strings_are_read_but_nonfinite_snr_is_not_promoted()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin_id = Observer,
                type = "PACKET",
                direction = "rx",
                RSSI = "-90",
                SNR = "NaN",
            }
        );
        var result = Success(manager.Process(Input(bytes)));
        Assert.Equal(-90, result.Report.Rssi);
        Assert.Null(result.Report.Snr);
    }

    [Fact]
    public void Wrong_raw_field_type_is_not_silently_absent()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                origin_id = Observer,
                type = "PACKET",
                direction = "rx",
                raw = 42,
            }
        );
        Assert.Equal(
            ReportError.InvalidRawField,
            manager
                .Process(Input(bytes))
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected rejection")
                )
        );
    }

    private IPacketProcessingManager.Result Process(string? raw, string? timestamp = null) =>
        Success(
            manager.Process(
                Input(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            origin_id = Observer,
                            type = "PACKET",
                            direction = "rx",
                            raw,
                            timestamp,
                        }
                    )
                )
            )
        );
}
