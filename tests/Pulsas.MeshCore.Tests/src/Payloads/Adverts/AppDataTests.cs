using Funcky.Monads;
using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Adverts;

public class AppDataTests
{
    [Theory]
    [InlineData("12", AdvertAppDataDecodeErrorKind.Malformed, false, null)]
    [InlineData("02aa", AdvertAppDataDecodeErrorKind.Unsupported, true, null)]
    [InlineData("a23412ff", AdvertAppDataDecodeErrorKind.Malformed, true, 0x1234)]
    [InlineData("7234127856aa", AdvertAppDataDecodeErrorKind.Malformed, false, null)]
    [InlineData(
        "000000000000000000000000000000000000000000000000000000000000000000",
        AdvertAppDataDecodeErrorKind.Malformed,
        false,
        null
    )]
    public void Failures_preserve_only_complete_fixed_claims(
        string hex,
        AdvertAppDataDecodeErrorKind kind,
        bool hasPartialData,
        int? feature1
    )
    {
        var bytes = Convert.FromHexString(hex);
        var error = Assert.IsType<AdvertAppDataDecodeError>(
            AdvertAppDataCodec
                .Decode(bytes)
                .Match<object>(left: value => value, right: value => value)
        );
        var partial = hasPartialData
            ? new AdvertAppData(NodeRole.Repeater, Feature1: (ushort?)feature1)
            : null;
        Assert.Equal(new AdvertAppDataDecodeError(kind, partial), error);
        Assert.False(AdvertAppDataCodec.TryDecode(bytes, out var data));
        Assert.Null(data);
    }

    [Theory]
    [InlineData("f2ffffffff0000000034127856ff", AdvertAppDataDecodeErrorKind.Malformed)]
    [InlineData("72ffffffff0000000034127856aa", AdvertAppDataDecodeErrorKind.Unsupported)]
    public void Name_and_trailing_data_failures_preserve_coordinates_and_both_features(
        string hex,
        AdvertAppDataDecodeErrorKind kind
    )
    {
        var expected = new AdvertAppDataDecodeError(
            kind,
            new(NodeRole.Repeater, -1, 0, 0x1234, 0x5678)
        );
        Assert.Equal(
            expected,
            AdvertAppDataCodec
                .Decode(Convert.FromHexString(hex))
                .Match<object>(left: error => error, right: data => data)
        );
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData("02", true, null)]
    [InlineData("82", true, "")]
    public void Empty_bytes_absent_name_and_empty_name_remain_distinct(
        string hex,
        bool hasClaims,
        string? name
    )
    {
        var bytes = Convert.FromHexString(hex);
        var expected = hasClaims
            ? Option.Some(new AdvertAppData(NodeRole.Repeater, Name: name))
            : Option<AdvertAppData>.None;
        Assert.Equal(
            expected,
            AdvertAppDataCodec
                .Decode(bytes)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.True(AdvertAppDataCodec.TryDecode(bytes, out var adapted));
        Assert.Equal(hasClaims ? new AdvertAppData(NodeRole.Repeater, Name: name) : null, adapted);
        Assert.Equal(
            bytes,
            AdvertAppDataCodec
                .Encode(adapted)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void All_role_nibbles_round_trip_without_losing_unknown_codes()
    {
        for (byte role = 0; role < 16; role++)
        {
            var data = Assert.IsType<AdvertAppData>(
                AdvertAppDataCodec
                    .Decode([role])
                    .Match<object?>(
                        left: error => error,
                        right: value =>
                            value.Match<AdvertAppData?>(none: () => null, some: claims => claims)
                    )
            );
            Assert.Equal(role, data.RoleCode);
            Assert.Equal(
                new[] { role },
                AdvertAppDataCodec
                    .Encode(data)
                    .Match<object>(left: error => error, right: value => value)
            );
        }
    }

    [Fact]
    public void Encodes_known_layout_with_optional_features_and_signed_integer_coordinates()
    {
        var input = new AdvertAppData(NodeRole.Repeater, -1, 0, 0x1234, 0x5678, "R1");
        var bytes = Assert.IsType<byte[]>(
            AdvertAppDataCodec
                .Encode(input)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal("f2ffffffff00000000341278565231", Convert.ToHexStringLower(bytes));
        Assert.True(AdvertAppDataCodec.TryDecode(bytes, out var decoded));
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Preserves_unknown_role_and_geographically_invalid_claims()
    {
        var input = new AdvertAppData((NodeRole)15, int.MaxValue, int.MinValue, Name: "Ž");
        var bytes = Assert.IsType<byte[]>(
            AdvertAppDataCodec
                .Encode(input)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(
            Option.Some(input),
            AdvertAppDataCodec
                .Decode(bytes)
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData(16)]
    [InlineData(255)]
    public void Encode_rejects_role_outside_four_bits(byte role)
    {
        Assert.Equal(
            new AdvertAppDataEncodeError(AdvertAppDataEncodeErrorKind.InvalidRole),
            AdvertAppDataCodec
                .Encode(new((NodeRole)role))
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    public void Encode_requires_both_coordinates(int? latitude, int? longitude)
    {
        Assert.Equal(
            new AdvertAppDataEncodeError(AdvertAppDataEncodeErrorKind.IncompleteCoordinates),
            AdvertAppDataCodec
                .Encode(new(NodeRole.Repeater, latitude, longitude))
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void Encode_returns_invalid_name_for_unpaired_surrogate()
    {
        Assert.Equal(
            new AdvertAppDataEncodeError(AdvertAppDataEncodeErrorKind.InvalidName),
            AdvertAppDataCodec
                .Encode(new(NodeRole.Repeater, Name: "\ud800"))
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData("x", 31, false, 32)]
    [InlineData("Ž", 15, false, 31)]
    [InlineData("x", 19, true, 32)]
    [InlineData("Ž", 9, true, 31)]
    public void Encode_enforces_total_UTF8_byte_limit_including_optional_fields(
        string character,
        int count,
        bool fields,
        int length
    )
    {
        var name = string.Concat(Enumerable.Repeat(character, count));
        var input = new AdvertAppData(
            NodeRole.Repeater,
            fields ? -1 : null,
            fields ? 0 : null,
            fields ? (ushort)1 : null,
            fields ? (ushort)2 : null,
            name
        );
        var bytes = Assert.IsType<byte[]>(
            AdvertAppDataCodec
                .Encode(input)
                .Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(length, bytes.Length);
        Assert.True(AdvertAppDataCodec.TryDecode(bytes, out var decoded));
        Assert.Equal(input, decoded);
        var oversized = input with { Name = name + character };
        Assert.Equal(
            new AdvertAppDataEncodeError(AdvertAppDataEncodeErrorKind.InvalidLength, 33),
            AdvertAppDataCodec
                .Encode(oversized)
                .Match<object>(left: error => error, right: value => value)
        );
    }
}
