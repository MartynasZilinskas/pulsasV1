using Pulsas.MeshCore.Payloads.Control;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Control;

public class ControlTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(183)]
    public void Preserves_every_subtype_and_opaque_body(int bodyLength)
    {
        for (var code = 0; code <= byte.MaxValue; code++)
        {
            byte[] bytes = [(byte)code, .. new byte[bodyLength]];
            var control = ControlCodec.Decode(bytes);
            Assert.Equal((byte)code, control.SubtypeCode);
            Assert.Equal(bytes, ControlCodec.Encode(control));
        }
        Assert.Equal(ControlSubtype.DiscoveryRequest, ControlCodec.Decode([8]).Subtype);
        Assert.Equal(ControlSubtype.DiscoveryResponse, ControlCodec.Decode([9]).Subtype);
    }

    [Fact]
    public void Body_is_copied_without_claiming_discovery_body_validation()
    {
        byte[] bytes = [8, 0, 0x12, 0, 0x12];
        var control = ControlCodec.Decode(bytes);
        bytes[1] = 99;
        Assert.Equal(new byte[] { 8, 0, 0x12, 0, 0x12 }, ControlCodec.Encode(control));
    }

    [Fact]
    public void Rejects_invalid_structure_and_construction()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[185] })
        {
            Assert.False(ControlCodec.TryDecode(bytes, out var result));
            Assert.Null(result);
            Assert.Throws<FormatException>(() => ControlCodec.Decode(bytes));
        }
        Assert.Throws<ArgumentException>(() =>
            ControlCodec.Encode(new(ControlSubtype.DiscoveryRequest, default))
        );
        Assert.Throws<ArgumentException>(() =>
            ControlCodec.Encode(new(ControlSubtype.DiscoveryRequest, [.. new byte[184]]))
        );
    }
}
