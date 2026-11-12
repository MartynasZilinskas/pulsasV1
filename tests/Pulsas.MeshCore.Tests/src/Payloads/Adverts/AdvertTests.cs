using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;

namespace Pulsas.MeshCore.Tests.Payloads.Adverts;

public class AdvertTests
{
    [Theory]
    [InlineData(
        0,
        "5339ad035b8c52c2698c00d1f227e63660544c0ad74d6d4590262bc22a7403eca9f8b58912d1c03d8a9b7badf82ec55e972b2034edaf6836ecf158590be18b06"
    )]
    [InlineData(
        8,
        "4a38058c25a4c0703e335480f0c4bc54174293bbc072c877ea7969d418e9d16e0a8000dfa46ebb21ccbf6376625ed18cf2ca3502745e81498abd316b51d7eb0e"
    )]
    [InlineData(
        32,
        "05b7576579e6d0108004fea284ced4fc7694ad8568cb135b568ce82087f4adbb37228eff393e0be32678d34bbb808dce009124a63e82305a2103bf9044d81701"
    )]
    public void Signs_exact_independent_OpenSSL_vectors(int length, string expectedSignature)
    {
        using var signer = AdvertSigningKey.ImportSeed(
            Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()
        );
        var app = new byte[length];
        if (length > 0)
        {
            Convert.FromHexString("8266697874757265").CopyTo(app, 0);
        }

        var advert = AdvertCodec.CreateSigned(signer, 0x04030201, app);
        Assert.Equal(
            "03a107bff3ce10be1d70dd18e74bc09967e4d6309ba50d5f1ddc8664125531b8",
            Convert.ToHexStringLower(advert.PublicKey.AsSpan())
        );
        Assert.Equal(expectedSignature, Convert.ToHexStringLower(advert.Signature.AsSpan()));
        var packet = PacketCodec.Create(
            RouteType.Flood,
            PacketType.Advert,
            AdvertCodec.Encode(advert)
        );
        var parsed = Assert.IsType<AdvertPayload>(
            PacketCodec
                .Decode(PacketCodec.Encode(packet))
                .Select(received => AdvertCodec.Decode(received.Payload.AsSpan()))
                .Match<object>(left: error => error, right: received => received)
        );
        Assert.True(AdvertCodec.Verify(parsed));
        var tampered = AdvertCodec.Encode(parsed);
        tampered[32] ^= 1;
        Assert.False(AdvertCodec.Verify(AdvertCodec.Decode(tampered)));
    }

    [Fact]
    public void Generated_keys_verify_and_private_material_is_not_exported_or_serialized()
    {
        using var signer = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(signer, 0, []);
        Assert.True(AdvertCodec.Verify(advert));
        var json = System.Text.Json.JsonSerializer.Serialize(signer);
        Assert.Contains("PublicKey", json);
        Assert.DoesNotContain("Private", json);
        Assert.DoesNotContain("Seed", json);
        signer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => signer.Sign([1]));
        signer.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(36)]
    [InlineData(100)]
    public void Mutations_are_not_authenticated(int offset)
    {
        using var signer = AdvertSigningKey.Generate();
        var bytes = AdvertCodec.Encode(AdvertCodec.CreateSigned(signer, uint.MaxValue, [2]));
        bytes[offset] ^= 1;
        Assert.False(AdvertCodec.Verify(AdvertCodec.Decode(bytes)));
    }

    [Fact]
    public void Rejects_malformed_construction_and_payloads()
    {
        using var signer = AdvertSigningKey.Generate();
        Assert.False(AdvertCodec.TryDecode(new byte[99], out _));
        Assert.False(AdvertCodec.TryDecode(new byte[133], out _));
        Assert.Throws<FormatException>(() => AdvertCodec.Decode([]));
        Assert.Throws<ArgumentException>(() => AdvertSigningKey.ImportSeed(new byte[31]));
        Assert.Throws<ArgumentException>(() => AdvertCodec.CreateSigned(signer, 0, new byte[33]));
        Assert.Throws<ArgumentException>(() => AdvertCodec.Encode(new([], 0, [], [])));
    }
}
