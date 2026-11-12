using Funcky.Monads;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Acks;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Acks;

public class AckPipelineTests
{
    [Theory]
    [InlineData("0d0000120012")]
    [InlineData("0dc10001020300120012")]
    public void Composes_framing_payload_decode_and_encode(string hex)
    {
        var encoded = DecodeAckFrame(Convert.FromHexString(hex))
            .SelectMany(ack =>
                AckCodec.Encode(ack).SelectLeft<AckFrameError>(error => new InvalidAck(error))
            )
            .Match<object>(left: error => error, right: bytes => bytes);

        Assert.Equal(new byte[] { 0, 0x12, 0, 0x12 }, Assert.IsType<byte[]>(encoded));
    }

    [Fact]
    public void Maps_errors_at_the_owning_boundary_without_losing_details()
    {
        Assert.Equal(new Framing(new(PacketDecodeErrorKind.InvalidFrameLength, 1)), Failure("0d"));
        // A four-byte body is not enough to identify an ACK.
        Assert.Equal(new UnexpectedPacket(PacketType.Advert), Failure("110000120012"));
        Assert.Equal(new UnsupportedVersion(1), Failure("4d0000120012"));
        Assert.Equal(
            new InvalidAck(new(AckErrorKind.InvalidChecksumLength, 3)),
            Failure("0d00001200")
        );
        Assert.Equal(
            new InvalidAck(new(AckErrorKind.InvalidChecksumLength, 5)),
            Failure("0d000012001200")
        );
    }

    [Fact]
    public void Framing_failure_short_circuits_interpretation_and_success_side_effects()
    {
        var calls = 0;
        var result = PacketCodec
            .Decode([0x0d])
            .SelectLeft<AckFrameError>(error => new Framing(error))
            .SelectMany(packet =>
            {
                calls++;
                return DecodeAckPacket(packet);
            })
            .Inspect(_ => calls++);

        Assert.IsType<Framing>(result.Match<object>(left: error => error, right: ack => ack));
        Assert.Equal(0, calls);
    }

    private static Either<AckFrameError, AckPayload> DecodeAckFrame(byte[] bytes) =>
        PacketCodec
            .Decode(bytes)
            .SelectLeft<AckFrameError>(error => new Framing(error))
            .SelectMany(DecodeAckPacket);

    private static Either<AckFrameError, AckPayload> DecodeAckPacket(MeshCorePacket packet)
    {
        if (packet.PacketType != PacketType.Ack)
        {
            return Either<AckFrameError, AckPayload>.Left(new UnexpectedPacket(packet.PacketType));
        }

        if (!packet.IsSupportedVersion)
        {
            return Either<AckFrameError, AckPayload>.Left(
                new UnsupportedVersion(packet.PayloadVersion)
            );
        }

        return AckCodec
            .Decode(packet.Payload.AsSpan())
            .SelectLeft<AckFrameError>(error => new InvalidAck(error));
    }

    private static AckFrameError Failure(string hex) =>
        Assert.IsAssignableFrom<AckFrameError>(
            DecodeAckFrame(Convert.FromHexString(hex))
                .Match<object>(left: error => error, right: ack => ack)
        );

    private abstract record AckFrameError;

    private record Framing(PacketDecodeError Error) : AckFrameError;

    private record UnexpectedPacket(PacketType PacketType) : AckFrameError;

    private record UnsupportedVersion(int Version) : AckFrameError;

    private record InvalidAck(AckError Error) : AckFrameError;
}
