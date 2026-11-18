using System.Collections.Immutable;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.PacketProcessing.Core.Paths;
using Xunit;

namespace Pulsas.PacketProcessing.Tests;

public class PathAssessmentManagerTests
{
    [Theory]
    [InlineData(PacketType.Advert, false, true)]
    [InlineData(PacketType.Advert, true, false)]
    [InlineData(PacketType.Path, false, false)]
    public void Advertiser_requires_advert_packet_and_valid_signature(
        PacketType packetType,
        bool tamperSignature,
        bool expectedVerified
    )
    {
        using var sender = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(sender, 1, []);
        var payload = AdvertCodec
            .Encode(advert)
            .Match(
                left: error => throw new InvalidOperationException(error.ToString()),
                right: bytes => bytes
            );
        if (tamperSignature)
        {
            payload[36] ^= 1;
        }
        var frame = PacketCodec.Create(RouteType.Flood, packetType, payload);

        var result = PathAssessmentManager.VerifiedAdvertiser(frame);

        Assert.Equal(expectedVerified, result.Match(none: () => false, some: _ => true));
        if (expectedVerified)
        {
            Assert.Equal(
                Convert.ToHexStringLower(sender.PublicKey.AsSpan()),
                result.Match(
                    none: () => throw new InvalidOperationException("Expected advertiser"),
                    some: key => key
                )
            );
        }
    }

    [Fact]
    public void Malformed_advert_has_no_verified_advertiser()
    {
        var frame = PacketCodec.Create(RouteType.Flood, PacketType.Advert, [1]);

        var result = PathAssessmentManager.VerifiedAdvertiser(frame);

        Assert.True(result.Match(none: () => true, some: _ => false));
    }

    [Theory]
    [InlineData(0, false, PathAssessmentStatus.Empty)]
    [InlineData(1, false, PathAssessmentStatus.Unresolved)]
    [InlineData(1, true, PathAssessmentStatus.Resolved)]
    [InlineData(2, false, PathAssessmentStatus.Unresolved)]
    [InlineData(2, true, PathAssessmentStatus.Partial)]
    public void Hop_outcomes_produce_aggregate_status(
        int count,
        bool oneResolved,
        PathAssessmentStatus expected
    )
    {
        var hops = Enumerable
            .Range(0, count)
            .Select(i => new PathHopAssessment(
                i,
                1,
                "ab",
                i == 0 && oneResolved ? PathHopOutcome.Resolved : PathHopOutcome.Unknown,
                []
            ))
            .ToImmutableArray();

        Assert.Equal(expected, PathAssessmentManager.GetStatus(hops));
    }
}
