using System.Collections.Immutable;
using NSec.Cryptography;
using Pulsas.MeshCore.Abstractions;

namespace Pulsas.MeshCore.Cryptography;

public class AdvertSigningKey : IAdvertSigner, IDisposable
{
    private readonly Key key;
    private bool disposed;

    private AdvertSigningKey(Key key)
    {
        this.key = key;
        PublicKey = [.. key.PublicKey.Export(KeyBlobFormat.RawPublicKey)];
    }

    public ImmutableArray<byte> PublicKey { get; }

    public static AdvertSigningKey Generate() => new(Key.Create(SignatureAlgorithm.Ed25519));

    // A seed is 32 bytes, not a firmware-specific expanded/private-key serialization.
    // The caller remains responsible for clearing its input buffer.
    public static AdvertSigningKey ImportSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
        {
            throw new ArgumentException("Ed25519 seeds must be 32 bytes.", nameof(seed));
        }

        return new(Key.Import(SignatureAlgorithm.Ed25519, seed, KeyBlobFormat.RawPrivateKey));
    }

    public ImmutableArray<byte> Sign(ReadOnlySpan<byte> signedMaterial)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return [.. SignatureAlgorithm.Ed25519.Sign(key, signedMaterial)];
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        key.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
