using System.Collections.Immutable;

namespace Pulsas.MeshCore.Payloads.Acks;

// A wire checksum, not evidence that a particular message was acknowledged.
public record AckPayload(ImmutableArray<byte> Checksum);
