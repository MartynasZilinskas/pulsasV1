using Pulsas.MeshCore.Packets;
using Xunit;

namespace Pulsas.MeshCore.Tests.Packets;

public class PacketTests
{
    [Theory]
    [InlineData(RouteType.Flood, 1)]
    [InlineData(RouteType.Direct, 2)]
    [InlineData(RouteType.TransportFlood, 3)]
    [InlineData(RouteType.TransportDirect, 2)]
    public void Preserves_route_width_transport_leading_zeros_and_repeated_positions(
        RouteType route,
        int width
    )
    {
        byte[] prefix = [.. Enumerable.Repeat((byte)0, width - 1), 0xab];
        byte[] path = [.. prefix, .. prefix];
        byte[] transport = route is RouteType.TransportFlood or RouteType.TransportDirect
            ? [1, 2, 0, 4]
            : [];
        var packet = PacketCodec.Create(
            route,
            PacketType.GroupText,
            [0xaa],
            path,
            width,
            transport
        );
        byte[] expected =
        [
            (byte)((5 << 2) | (byte)route),
            .. transport,
            (byte)(((width - 1) << 6) | 2),
            .. path,
            0xaa,
        ];
        Assert.Equal(expected, PacketCodec.Encode(packet));
        var decoded = Decode(expected);
        Assert.Equal(route, decoded.RouteType);
        Assert.Equal(PacketType.GroupText, decoded.PacketType);
        Assert.Equal(width, decoded.PathWidth);
        Assert.Equal(2, decoded.PathCount);
        Assert.Equal(path, decoded.Path.ToArray());
        Assert.Equal(transport, decoded.TransportCodes.ToArray());
    }

    [Fact]
    public void Preserves_future_versions_reserved_widths_unknown_types_and_trace_without_interpreting_them()
    {
        byte[] opaque = [0xf5, 0xc1, 0, 1, 2, 3, 0xff]; // v3, reserved type13, width4
        var parsed = Decode(opaque);
        Assert.False(parsed.IsSupportedVersion);
        Assert.False(parsed.IsSupportedPathWidth);
        Assert.Equal((PacketType)13, parsed.PacketType);
        Assert.Equal(opaque, PacketCodec.Encode(parsed));
        byte[] trace = [0x26, 1, 0xfc, 1, 2, 3];
        Assert.Equal(trace, PacketCodec.Encode(Decode(trace)));
    }

    [Theory]
    [InlineData("", PacketDecodeErrorKind.InvalidFrameLength, 0)]
    [InlineData("11", PacketDecodeErrorKind.InvalidFrameLength, 1)]
    [InlineData("1100", PacketDecodeErrorKind.InvalidFrameLength, 2)]
    [InlineData("1000000000", PacketDecodeErrorKind.TruncatedFraming, 5)]
    [InlineData("100000", PacketDecodeErrorKind.TruncatedFraming, 3)]
    [InlineData("100000000000", PacketDecodeErrorKind.InvalidPayloadLength, 0)]
    [InlineData("110100", PacketDecodeErrorKind.InvalidPayloadLength, 0)]
    [InlineData("11bf00", PacketDecodeErrorKind.InvalidPathLength, 189)]
    [InlineData("110300", PacketDecodeErrorKind.InvalidPayloadLength, -2)]
    public void Rejects_truncated_or_inconsistent_frames(
        string hex,
        PacketDecodeErrorKind kind,
        int length
    )
    {
        var bytes = Convert.FromHexString(hex);
        Assert.False(PacketCodec.TryDecode(bytes, out var result));
        Assert.Null(result);
        var error = Assert.IsType<PacketDecodeError>(
            PacketCodec.Decode(bytes).Match<object>(left: error => error, right: packet => packet)
        );
        Assert.Equal(kind, error.Kind);
        Assert.Equal(length, error.Length);
        Assert.Equal(
            kind == PacketDecodeErrorKind.TruncatedFraming ? 7 : (int?)null,
            error.MinimumLength
        );
    }

    [Fact]
    public void Enforces_lengths_and_copies_mutable_inputs()
    {
        var payload = new byte[184];
        var path = new byte[64];
        var value = PacketCodec.Create(
            RouteType.TransportFlood,
            PacketType.RawCustom,
            payload,
            path,
            2,
            [0, 0, 0, 0]
        );
        var raw = PacketCodec.Encode(value);
        var decoded = Decode(raw);
        raw[^1] = payload[0] = path[0] = 99;
        Assert.Equal(0, decoded.Payload[^1]);
        Assert.Equal(0, value.Payload[0]);
        Assert.Equal(0, value.Path[0]);
        Assert.Throws<ArgumentException>(() =>
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, new byte[185])
        );
        Assert.Throws<ArgumentException>(() =>
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, [1], path)
        ); // 64 one-byte entries
        Assert.Throws<ArgumentException>(() =>
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, [1], [0], 2)
        );
        Assert.Throws<ArgumentException>(() =>
            PacketCodec.Create(
                RouteType.Flood,
                PacketType.Advert,
                [1],
                transportCodes: [0, 0, 0, 0]
            )
        );
        Assert.Throws<ArgumentException>(() =>
            PacketCodec.Create(RouteType.TransportFlood, PacketType.Advert, [1])
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PacketCodec.Create(RouteType.Flood, PacketType.Advert, [1], pathWidth: 4)
        );
        Assert.False(PacketCodec.TryDecode([0x11, 0, .. new byte[185]], out _));
        Assert.False(PacketCodec.TryDecode(new byte[256], out _));
    }

    [Theory]
    [InlineData(184)]
    [InlineData(185)]
    public void Reports_payload_length_boundaries(int length)
    {
        var result = PacketCodec.Decode([0x11, 0, .. new byte[length]]);
        result.Switch(
            left: error =>
                Assert.Equal(new(PacketDecodeErrorKind.InvalidPayloadLength, 185), error),
            right: packet => Assert.Equal(184, packet.Payload.Length)
        );
        Assert.Equal(length == 184, result.Match(left: _ => false, right: _ => true));
    }

    [Fact]
    public void Reports_oversized_frame() =>
        Assert.Equal(
            new(PacketDecodeErrorKind.InvalidFrameLength, 256),
            Assert.IsType<PacketDecodeError>(
                PacketCodec
                    .Decode(new byte[256])
                    .Match<object>(left: error => error, right: packet => packet)
            )
        );

    [Fact]
    public void Decodes_only_the_supplied_span_and_owns_its_bytes()
    {
        byte[] storage = [0xff, 0x10, 1, 2, 3, 4, 1, 0xab, 0xcd, 0xff];
        var packet = Assert.IsType<MeshCorePacket>(
            PacketCodec
                .Decode(storage.AsSpan(1, 8))
                .Match<object>(left: error => error, right: packet => packet)
        );

        storage.AsSpan().Clear();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, packet.TransportCodes.ToArray());
        Assert.Equal(new byte[] { 0xab }, packet.Path.ToArray());
        Assert.Equal(new byte[] { 0xcd }, packet.Payload.ToArray());
        Assert.Equal(new byte[] { 0x10, 1, 2, 3, 4, 1, 0xab, 0xcd }, PacketCodec.Encode(packet));
    }

    private static MeshCorePacket Decode(byte[] bytes)
    {
        Assert.True(PacketCodec.TryDecode(bytes, out var packet));
        return packet;
    }
}
