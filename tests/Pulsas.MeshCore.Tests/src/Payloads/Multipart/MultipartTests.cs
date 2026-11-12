using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Multipart;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Multipart;

public class MultipartTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(183)]
    public void Preserves_all_discriminators_counts_and_opaque_bodies(int bodyLength)
    {
        for (byte type = 0; type < 16; type++)
        {
            for (byte remaining = 0; remaining < 16; remaining++)
            {
                byte[] bytes = [(byte)((remaining << 4) | type), .. new byte[bodyLength]];
                var multipart = MultipartCodec.Decode(bytes);
                Assert.Equal(remaining, multipart.Remaining);
                Assert.Equal((PacketType)type, multipart.PayloadType);
                Assert.Equal(bytes, MultipartCodec.Encode(multipart));
            }
        }
    }

    [Fact]
    public void Body_is_copied_and_not_validated_as_the_inner_type()
    {
        byte[] bytes = [0x33, 0, 0x12, 0, 0x12, 0]; // ACK discriminator, but not a four-byte ACK.
        var multipart = MultipartCodec.Decode(bytes);
        bytes[1] = 99;
        Assert.Equal(new byte[] { 0x33, 0, 0x12, 0, 0x12, 0 }, MultipartCodec.Encode(multipart));
        Assert.Empty(MultipartCodec.Decode([3]).Body);
    }

    [Fact]
    public void Rejects_invalid_structure_and_construction()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[185] })
        {
            Assert.False(MultipartCodec.TryDecode(bytes, out var result));
            Assert.Null(result);
            Assert.Throws<FormatException>(() => MultipartCodec.Decode(bytes));
        }
        Assert.Throws<ArgumentException>(() => MultipartCodec.Encode(new(16, PacketType.Ack, [])));
        Assert.Throws<ArgumentException>(() => MultipartCodec.Encode(new(0, (PacketType)16, [])));
        Assert.Throws<ArgumentException>(() =>
            MultipartCodec.Encode(new(0, PacketType.Ack, default))
        );
        Assert.Throws<ArgumentException>(() =>
            MultipartCodec.Encode(new(0, PacketType.Ack, [.. new byte[184]]))
        );
    }
}
