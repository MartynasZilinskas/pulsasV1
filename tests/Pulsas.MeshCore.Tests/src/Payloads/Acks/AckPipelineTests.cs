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
    public void Composes_packet_payload_decode_and_encode(string hex)
    {
        var encoded = DecodeAckPacketBytes(Convert.FromHexString(hex))
            .SelectMany(ack =>
                AckCodec.Encode(ack).SelectLeft<AckPacketError>(error => new InvalidAck(error))
            )
            .Match<object>(left: error => error, right: bytes => bytes);

        Assert.Equal(new byte[] { 0, 0x12, 0, 0x12 }, Assert.IsType<byte[]>(encoded));
    }

    [Fact]
    public void Maps_errors_at_the_owning_boundary_without_losing_details()
    {
        Assert.Equal(
            new PacketDecoding(new(PacketDecodeErrorKind.InvalidPacketLength, 1)),
            Failure("0d")
        );
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
    public void Packet_decode_failure_short_circuits_interpretation_and_success_side_effects()
    {
        var calls = 0;
        var result = PacketCodec
            .Decode([0x0d])
            .SelectLeft<AckPacketError>(error => new PacketDecoding(error))
            .SelectMany(packet =>
            {
                calls++;
                return DecodeAckPacket(packet);
            })
            .Inspect(_ => calls++);

        Assert.IsType<PacketDecoding>(
            result.Match<object>(left: error => error, right: ack => ack)
        );
        Assert.Equal(0, calls);
    }

    private static Either<AckPacketError, AckPayload> DecodeAckPacketBytes(byte[] bytes) =>
        PacketCodec
            .Decode(bytes)
            .SelectLeft<AckPacketError>(error => new PacketDecoding(error))
            .SelectMany(DecodeAckPacket);

    private static Either<AckPacketError, AckPayload> DecodeAckPacket(MeshCorePacket packet)
    {
        if (packet.PacketType != PacketType.Ack)
        {
            return Either<AckPacketError, AckPayload>.Left(new UnexpectedPacket(packet.PacketType));
        }

        if (!packet.IsSupportedVersion)
        {
            return Either<AckPacketError, AckPayload>.Left(
                new UnsupportedVersion(packet.PayloadVersion)
            );
        }

        return AckCodec
            .Decode(packet.Payload.AsSpan())
            .SelectLeft<AckPacketError>(error => new InvalidAck(error));
    }

    private static AckPacketError Failure(string hex) =>
        Assert.IsAssignableFrom<AckPacketError>(
            DecodeAckPacketBytes(Convert.FromHexString(hex))
                .Match<object>(left: error => error, right: ack => ack)
        );

    private abstract record AckPacketError;

    private record PacketDecoding(PacketDecodeError Error) : AckPacketError;

    private record UnexpectedPacket(PacketType PacketType) : AckPacketError;

    private record UnsupportedVersion(int Version) : AckPacketError;

    private record InvalidAck(AckError Error) : AckPacketError;
}
