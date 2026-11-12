using System.Collections.Immutable;

namespace Pulsas.MeshCore.Abstractions;

// Caller owns signer lifetime. Implementations must not disclose private key material.
public interface IAdvertSigner
{
    ImmutableArray<byte> PublicKey { get; }
    ImmutableArray<byte> Sign(ReadOnlySpan<byte> signedMaterial);
}
