using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
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
        advert = null;
        if (payload.Length is < 100 or > 132)
        {
            return false;
        }

        advert = new(
            [.. payload[..32]],
            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..36]),
            [.. payload[36..100]],
            [.. payload[100..]]
        );
        return true;
    }

    public static AdvertPayload Decode(ReadOnlySpan<byte> payload) =>
        TryDecode(payload, out var advert)
            ? advert
            : throw new FormatException("Malformed ADVERT payload.");

    public static byte[] Encode(AdvertPayload advert)
    {
        var signed = GetSignedMaterial(advert);
        return [.. signed.AsSpan(0, 36), .. advert.Signature, .. advert.AppData];
    }

    public static byte[] GetSignedMaterial(AdvertPayload advert)
    {
        ArgumentNullException.ThrowIfNull(advert);
        if (
            advert.PublicKey.IsDefault
            || advert.PublicKey.Length != 32
            || advert.Signature.IsDefault
            || advert.Signature.Length != 64
            || advert.AppData.IsDefault
            || advert.AppData.Length > MaximumAppDataLength
        )
        {
            throw new ArgumentException("Invalid ADVERT field lengths.", nameof(advert));
        }

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
        ArgumentNullException.ThrowIfNull(signer);
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
        var algorithm = SignatureAlgorithm.Ed25519;
        return PublicKey.TryImport(
                algorithm,
                advert.PublicKey.AsSpan(),
                KeyBlobFormat.RawPublicKey,
                out var key
            ) && algorithm.Verify(key!, signed, advert.Signature.AsSpan());
    }
}
