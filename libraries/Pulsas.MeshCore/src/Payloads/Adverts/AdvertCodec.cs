using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Funcky;
using Funcky.Monads;
using NSec.Cryptography;
using Pulsas.MeshCore.Abstractions;

namespace Pulsas.MeshCore.Payloads.Adverts;

public static class AdvertCodec
{
    public const int MaximumAppDataLength = 32;
    public const string ProtocolRevision = "81dbae592ffad40b4212bc0b37e82b38da08f4bc";

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out AdvertPayload? advert
    )
    {
        advert = Decode(payload).Match<AdvertPayload?>(left: _ => null, right: value => value);
        return advert is not null;
    }

    public static Either<AdvertDecodeError, AdvertPayload> Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 100 or > 132)
        {
            return Either<AdvertDecodeError, AdvertPayload>.Left(new(payload.Length));
        }

        return new AdvertPayload(
            [.. payload[..32]],
            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..36]),
            [.. payload[36..100]],
            [.. payload[100..]]
        );
    }

    public static byte[] Encode(AdvertPayload advert)
    {
        var signed = GetSignedMaterial(advert);
        return [.. signed.AsSpan(0, 36), .. advert.Signature, .. advert.AppData];
    }

    public static byte[] GetSignedMaterial(AdvertPayload advert)
    {
        if (ValidateFields(advert) is not null)
        {
            throw new ArgumentException("Invalid ADVERT field lengths.", nameof(advert));
        }

        return CreateSignedMaterial(advert);
    }

    private static byte[] CreateSignedMaterial(AdvertPayload advert)
    {
        var signed = new byte[36 + advert.AppData.Length];
        advert.PublicKey.AsSpan().CopyTo(signed);
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(32), advert.Timestamp);
        advert.AppData.AsSpan().CopyTo(signed.AsSpan(36));
        return signed;
    }

    public static AdvertPayload CreateSigned(
        IAdvertSigner signer,
        uint timestamp,
        ReadOnlySpan<byte> appData
    )
    {
        var advert = new AdvertPayload(
            signer.PublicKey,
            timestamp,
            [.. new byte[64]],
            [.. appData]
        );
        var signature = signer.Sign(GetSignedMaterial(advert));
        advert = advert with { Signature = signature };
        _ = GetSignedMaterial(advert);
        return advert;
    }

    public static bool Verify(AdvertPayload advert)
    {
        var signed = GetSignedMaterial(advert);
        return VerifySignature(advert, signed).Match(left: _ => false, right: _ => true);
    }

    // Success authenticates the signed ADVERT fields, not freshness, trust or packet routing.
    public static Either<AdvertSignatureError, Unit> ValidateSignature(AdvertPayload advert)
    {
        if (ValidateFields(advert) is { } error)
        {
            return Either<AdvertSignatureError, Unit>.Left(error);
        }

        return VerifySignature(advert, CreateSignedMaterial(advert));
    }

    private static Either<AdvertSignatureError, Unit> VerifySignature(
        AdvertPayload advert,
        ReadOnlySpan<byte> signed
    )
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        if (
            !PublicKey.TryImport(
                algorithm,
                advert.PublicKey.AsSpan(),
                KeyBlobFormat.RawPublicKey,
                out var key
            )
        )
        {
            return Either<AdvertSignatureError, Unit>.Left(
                new(AdvertSignatureErrorKind.PublicKeyImportRejected)
            );
        }

        return algorithm.Verify(key!, signed, advert.Signature.AsSpan())
            ? Either<AdvertSignatureError, Unit>.Right(default)
            : Either<AdvertSignatureError, Unit>.Left(
                new(AdvertSignatureErrorKind.SignatureMismatch)
            );
    }

    private static AdvertSignatureError? ValidateFields(AdvertPayload advert) =>
        advert switch
        {
            { PublicKey.IsDefault: true } => new(AdvertSignatureErrorKind.UninitializedPublicKey),
            { PublicKey.Length: not 32 } => new(
                AdvertSignatureErrorKind.InvalidPublicKeyLength,
                advert.PublicKey.Length
            ),
            { Signature.IsDefault: true } => new(AdvertSignatureErrorKind.UninitializedSignature),
            { Signature.Length: not 64 } => new(
                AdvertSignatureErrorKind.InvalidSignatureLength,
                advert.Signature.Length
            ),
            { AppData.IsDefault: true } => new(AdvertSignatureErrorKind.UninitializedAppData),
            { AppData.Length: > MaximumAppDataLength } => new(
                AdvertSignatureErrorKind.InvalidAppDataLength,
                advert.AppData.Length
            ),
            _ => null,
        };
}
