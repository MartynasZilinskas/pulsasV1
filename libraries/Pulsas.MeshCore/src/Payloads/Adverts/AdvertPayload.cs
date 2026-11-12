using System.Collections.Immutable;

namespace Pulsas.MeshCore.Payloads.Adverts;

public record AdvertPayload(
    ImmutableArray<byte> PublicKey,
    uint Timestamp,
    ImmutableArray<byte> Signature,
    ImmutableArray<byte> AppData
);
