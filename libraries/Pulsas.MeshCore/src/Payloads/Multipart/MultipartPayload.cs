using System.Collections.Immutable;
using Pulsas.MeshCore.Packets;

namespace Pulsas.MeshCore.Payloads.Multipart;

// Only the wrapper is interpreted; the body remains opaque, even for known types.
public record MultipartPayload(byte Remaining, PacketType PayloadType, ImmutableArray<byte> Body);
