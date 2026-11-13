using System.Text.Json;
using Pulsas.MeshCore.Cryptography;
using Pulsas.MeshCore.Packets;
using Pulsas.MeshCore.Payloads.Adverts;
using Xunit;
using Xunit.Sdk;

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
                .Select(received =>
                    AdvertCodec
                        .Decode(received.Payload.AsSpan())
                        .Match<object>(left: error => error, right: advert => advert)
                )
                .Match<object>(left: error => error, right: received => received)
        );
        Assert.True(AdvertCodec.Verify(parsed));
        Assert.True(
            AdvertCodec.ValidateSignature(parsed).Match(left: _ => false, right: _ => true)
        );
        var tampered = AdvertCodec.Encode(parsed);
        tampered[32] ^= 1;
        Assert.False(
            AdvertCodec
                .Decode(tampered)
                .Match(
                    left: _ => throw new XunitException("Expected structural decode success."),
                    right: AdvertCodec.Verify
                )
        );
    }

    [Fact]
    public void Generated_keys_verify_and_private_material_is_not_exported_or_serialized()
    {
        using var signer = AdvertSigningKey.Generate();
        var advert = AdvertCodec.CreateSigned(signer, 0, []);
        Assert.True(AdvertCodec.Verify(advert));
        var json = JsonSerializer.Serialize(signer);
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
        var decoded = AdvertCodec
            .Decode(bytes)
            .Match(
                left: _ => throw new XunitException("Expected structural decode success."),
                right: advert => advert
            );
        Assert.False(AdvertCodec.Verify(decoded));
        Assert.Equal(
            AdvertSignatureErrorKind.SignatureMismatch,
            AdvertCodec
                .ValidateSignature(decoded)
                .Match(
                    left: error => error.Kind,
                    right: _ => throw new XunitException("Expected signature failure.")
                )
        );
    }

    [Fact]
    public void Signature_validation_returns_typed_errors_for_malformed_fields()
    {
        using var signer = AdvertSigningKey.Generate();
        var valid = AdvertCodec.CreateSigned(signer, 0, []);
        (AdvertPayload Payload, AdvertSignatureError Error)[] cases =
        [
            (
                valid with
                {
                    PublicKey = default,
                },
                new(AdvertSignatureErrorKind.UninitializedPublicKey)
            ),
            (
                valid with
                {
                    PublicKey = [],
                },
                new(AdvertSignatureErrorKind.InvalidPublicKeyLength, 0)
            ),
            (
                valid with
                {
                    PublicKey = [.. new byte[33]],
                },
                new(AdvertSignatureErrorKind.InvalidPublicKeyLength, 33)
            ),
            (
                valid with
                {
                    Signature = default,
                },
                new(AdvertSignatureErrorKind.UninitializedSignature)
            ),
            (
                valid with
                {
                    Signature = [],
                },
                new(AdvertSignatureErrorKind.InvalidSignatureLength, 0)
            ),
            (
                valid with
                {
                    Signature = [.. new byte[65]],
                },
                new(AdvertSignatureErrorKind.InvalidSignatureLength, 65)
            ),
            (valid with { AppData = default }, new(AdvertSignatureErrorKind.UninitializedAppData)),
            (
                valid with
                {
                    AppData = [.. new byte[33]],
                },
                new(AdvertSignatureErrorKind.InvalidAppDataLength, 33)
            ),
        ];

        foreach (var (payload, expected) in cases)
        {
            var actual = AdvertCodec
                .ValidateSignature(payload)
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected validation failure.")
                );
            Assert.Equal(expected, actual);
            Assert.Throws<ArgumentException>(() => AdvertCodec.Verify(payload));
            Assert.Throws<ArgumentException>(() => AdvertCodec.GetSignedMaterial(payload));
            Assert.Throws<ArgumentException>(() => AdvertCodec.Encode(payload));
        }

        Assert.Empty(valid.AppData);
        Assert.True(AdvertCodec.ValidateSignature(valid).Match(left: _ => false, right: _ => true));
    }

    [Fact]
    public void Rejects_malformed_construction_and_payloads()
    {
        using var signer = AdvertSigningKey.Generate();
        Assert.False(AdvertCodec.TryDecode(new byte[99], out _));
        Assert.False(AdvertCodec.TryDecode(new byte[133], out _));
        Assert.Equal(
            new AdvertDecodeError(0),
            AdvertCodec
                .Decode([])
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected decode failure.")
                )
        );
        Assert.Throws<ArgumentException>(() => AdvertSigningKey.ImportSeed(new byte[31]));
        Assert.Throws<ArgumentException>(() => AdvertCodec.CreateSigned(signer, 0, new byte[33]));
        Assert.Throws<ArgumentException>(() => AdvertCodec.Encode(new([], 0, [], [])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(133)]
    public void Decode_returns_length_error_and_try_adapter_returns_null(int length)
    {
        Assert.Equal(
            new AdvertDecodeError(length),
            AdvertCodec
                .Decode(new byte[length])
                .Match(
                    left: error => error,
                    right: _ => throw new XunitException("Expected decode failure.")
                )
        );
        Assert.False(AdvertCodec.TryDecode(new byte[length], out var advert));
        Assert.Null(advert);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(132)]
    public void Decode_accepts_boundary_lengths_without_authenticating_signature(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var decoded = AdvertCodec
            .Decode(bytes)
            .Match(
                left: _ => throw new XunitException("Expected structural decode success."),
                right: advert => advert
            );
        Assert.Equal(bytes, AdvertCodec.Encode(decoded));
        Assert.True(AdvertCodec.TryDecode(bytes, out var adapted));
        Assert.Equal(decoded.Timestamp, adapted.Timestamp);
        Assert.Equal(decoded.PublicKey.ToArray(), adapted.PublicKey.ToArray());
        Assert.Equal(decoded.Signature.ToArray(), adapted.Signature.ToArray());
        Assert.Equal(decoded.AppData.ToArray(), adapted.AppData.ToArray());
        Assert.False(
            AdvertCodec.ValidateSignature(decoded).Match(left: _ => false, right: _ => true)
        );
    }
}
