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
            var control = Assert.IsType<ControlPayload>(
                ControlCodec
                    .Decode(bytes)
                    .Match<object>(left: error => error, right: value => value)
            );
            Assert.Equal((byte)code, control.SubtypeCode);
            Assert.Equal(
                bytes,
                ControlCodec
                    .Encode(control)
                    .Match<object>(left: error => error, right: value => value)
            );
        }
        Assert.Equal(
            ControlSubtype.DiscoveryRequest,
            ControlCodec
                .Decode([8])
                .Match(
                    left: _ => throw new Xunit.Sdk.XunitException("Expected decode success."),
                    right: control => control.Subtype
                )
        );
        Assert.Equal(
            ControlSubtype.DiscoveryResponse,
            ControlCodec
                .Decode([9])
                .Match(
                    left: _ => throw new Xunit.Sdk.XunitException("Expected decode success."),
                    right: control => control.Subtype
                )
        );
    }

    [Fact]
    public void Body_is_copied_without_claiming_discovery_body_validation()
    {
        byte[] bytes = [8, 0, 0x12, 0, 0x12];
        Assert.True(ControlCodec.TryDecode(bytes, out var control));
        bytes[1] = 99;
        Assert.Equal(
            new byte[] { 8, 0, 0x12, 0, 0x12 },
            ControlCodec.Encode(control).Match<object>(left: error => error, right: value => value)
        );
    }

    [Fact]
    public void Encode_rejects_uninitialized_body()
    {
        Assert.Equal(
            new ControlEncodeError(ControlEncodeErrorKind.UninitializedBody),
            ControlCodec
                .Encode(new(ControlSubtype.DiscoveryRequest, default))
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData(184)]
    [InlineData(256)]
    public void Encode_returns_rejected_body_length(int length)
    {
        Assert.Equal(
            new ControlEncodeError(ControlEncodeErrorKind.InvalidBodyLength, length),
            ControlCodec
                .Encode(new(ControlSubtype.DiscoveryRequest, [.. new byte[length]]))
                .Match<object>(left: error => error, right: value => value)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(185)]
    [InlineData(256)]
    public void Decode_returns_typed_length_error_and_try_adapter_returns_null(int length)
    {
        var bytes = new byte[length];
        var error = Assert.IsType<ControlDecodeError>(
            ControlCodec.Decode(bytes).Match<object>(left: value => value, right: value => value)
        );
        Assert.Equal(new ControlDecodeError(length), error);
        Assert.False(ControlCodec.TryDecode(bytes, out var result));
        Assert.Null(result);
    }
}
