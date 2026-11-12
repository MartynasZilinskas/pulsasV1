using Pulsas.MeshCore.Payloads.Acks;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Acks;

public class AckTests
{
    [Fact]
    public void Preserves_checksum_bytes_and_copies_input()
    {
        byte[] bytes = [0, 0x12, 0, 0x12];
        var ack = Assert.IsType<AckPayload>(
            AckCodec.Decode(bytes).Match<object>(left: error => error, right: ack => ack)
        );
        bytes[0] = 99;
        var encoded = Assert.IsType<byte[]>(
            AckCodec.Encode(ack).Match<object>(left: error => error, right: value => value)
        );
        Assert.Equal(new byte[] { 0, 0x12, 0, 0x12 }, encoded);
        Assert.Equal(new byte[] { 0, 0x12, 0, 0x12 }, ack.Checksum.ToArray());
        Assert.True(AckCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(ack.Checksum.ToArray(), decoded.Checksum.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rejects_non_four_byte_checksums(int length)
    {
        var bytes = new byte[length];
        Assert.False(AckCodec.TryDecode(bytes, out var result));
        Assert.Null(result);
        var expected = new AckError(AckErrorKind.InvalidChecksumLength, length);
        Assert.Equal(
            expected,
            Assert.IsType<AckError>(
                AckCodec.Decode(bytes).Match<object>(left: error => error, right: ack => ack)
            )
        );
        Assert.Equal(
            expected,
            Assert.IsType<AckError>(
                AckCodec
                    .Encode(new([.. bytes]))
                    .Match<object>(left: error => error, right: value => value)
            )
        );
    }

    [Fact]
    public void Rejects_uninitialized_checksum() =>
        Assert.Equal(
            new(AckErrorKind.UninitializedChecksum),
            Assert.IsType<AckError>(
                AckCodec
                    .Encode(new(default))
                    .Match<object>(left: error => error, right: value => value)
            )
        );

    [Fact]
    public void Null_model_is_an_invalid_API_invocation() =>
        Assert.Throws<ArgumentNullException>(() => AckCodec.Encode(null!));
}
