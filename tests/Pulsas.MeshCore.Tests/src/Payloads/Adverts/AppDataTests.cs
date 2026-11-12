using Pulsas.MeshCore.Nodes;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Adverts;

public class AppDataTests
{
    [Theory]
    [InlineData("", AdvertAppDataDecodeStatus.Empty)]
    [InlineData("12", AdvertAppDataDecodeStatus.Malformed)]
    [InlineData("02aa", AdvertAppDataDecodeStatus.Unsupported)]
    [InlineData("a23412ff", AdvertAppDataDecodeStatus.Malformed)]
    [InlineData("02", AdvertAppDataDecodeStatus.Decoded)]
    public void Detailed_outcomes_preserve_only_complete_claims(
        string hex,
        AdvertAppDataDecodeStatus status
    )
    {
        var bytes = Convert.FromHexString(hex);
        var result = AdvertAppDataCodec.Decode(bytes);
        Assert.Equal(status, result.Status);
        if (hex is "" or "12")
        {
            Assert.Null(result.Data);
        }
        else
        {
            Assert.Equal(NodeRole.Repeater, result.Data!.Role);
        }

        if (hex == "a23412ff")
        {
            Assert.Equal((ushort)0x1234, result.Data!.Feature1);
            Assert.Null(result.Data.Name);
        }
        if (status is AdvertAppDataDecodeStatus.Malformed or AdvertAppDataDecodeStatus.Unsupported)
        {
            Assert.False(AdvertAppDataCodec.TryDecode(bytes, out var data));
            Assert.Null(data);
        }
    }

    [Fact]
    public void All_role_nibbles_round_trip_without_losing_unknown_codes()
    {
        for (byte role = 0; role < 16; role++)
        {
            var result = AdvertAppDataCodec.Decode([role]);
            Assert.Equal(role, result.Data!.RoleCode);
            Assert.Equal(new[] { role }, AdvertAppDataCodec.Encode(result.Data));
        }
    }

    [Fact]
    public void Encodes_known_layout_with_optional_features_and_signed_integer_coordinates()
    {
        var input = new AdvertAppData(NodeRole.Repeater, -1, 0, 0x1234, 0x5678, "R1");
        var bytes = AdvertAppDataCodec.Encode(input);
        Assert.Equal("f2ffffffff00000000341278565231", Convert.ToHexStringLower(bytes));
        Assert.True(AdvertAppDataCodec.TryDecode(bytes, out var decoded));
        Assert.Equal(input, decoded);
        Assert.True(AdvertAppDataCodec.TryDecode([], out var empty));
        Assert.Null(empty);
        Assert.Empty(AdvertAppDataCodec.Encode(null));
    }

    [Fact]
    public void Preserves_unknown_role_and_geographically_invalid_claims()
    {
        var input = new AdvertAppData((NodeRole)15, int.MaxValue, int.MinValue, Name: "Ž");
        Assert.True(AdvertAppDataCodec.TryDecode(AdvertAppDataCodec.Encode(input), out var result));
        Assert.Equal(input, result);
    }

    [Fact]
    public void Distinguishes_absent_and_empty_name_and_rejects_malformed_structure()
    {
        Assert.Equal(new byte[] { 2 }, AdvertAppDataCodec.Encode(new(NodeRole.Repeater)));
        Assert.Equal(
            new byte[] { 0x82 },
            AdvertAppDataCodec.Encode(new(NodeRole.Repeater, Name: ""))
        );
        Assert.False(AdvertAppDataCodec.TryDecode([0x12], out _));
        Assert.False(AdvertAppDataCodec.TryDecode([0x82, 0xff], out _));
        Assert.False(AdvertAppDataCodec.TryDecode([2, 0], out _));
        Assert.False(AdvertAppDataCodec.TryDecode(new byte[33], out _));
        Assert.Throws<ArgumentException>(() => AdvertAppDataCodec.Encode(new((NodeRole)16)));
        Assert.Throws<ArgumentException>(() =>
            AdvertAppDataCodec.Encode(new(NodeRole.Repeater, LatitudeMicrodegrees: 0))
        );
        Assert.Throws<ArgumentException>(() =>
            AdvertAppDataCodec.Encode(new(NodeRole.Repeater, Name: new string('Ž', 16)))
        );
    }
}
