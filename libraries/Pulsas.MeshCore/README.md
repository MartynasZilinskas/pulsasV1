# Pulsas.MeshCore

MeshCore wire-format codecs for **.NET 11**, using **NSec.Cryptography 26.4.0**
and **Funcky 3.8.0**. No MQTT, hosting, or database dependencies.

## Supported

- Packet encoding/decoding, preserving paths and unknown wire codes.
- ADVERT encoding, decoding, Ed25519 signing/verification, and application data.
- Group text encryption/decryption and four-byte ACK checksums.
- Multipart/control wrapper headers with opaque bodies.

TRACE interpretation, addressed-message cryptography, forwarding, and RF
simulation are not implemented.

## Example

```csharp
using Pulsas.MeshCore.Packets;

byte[] wire = Convert.FromHexString("0D0000120012");
string outcome = PacketCodec.Decode(wire)
    .Match(
        left: error => $"Malformed packet: {error.Kind} (length {error.Length})",
        right: packet => $"{packet.PacketType}: {Convert.ToHexString(packet.Payload.AsSpan())}"
    );
Console.WriteLine(outcome);
```

## API notes

- `PacketCodec` handles packet encoding/decoding and preserves payload bytes without
  interpreting them. Payload codecs accept payload bytes, not full packets.
  Packet, ADVERT, ACK, control, multipart, and group-text decoding return typed
  Funcky `Either` results. App-data decoding returns
  `Either<AdvertAppDataDecodeError, Option<AdvertAppData>>`: empty bytes are a
  successful `None`, and failures can carry partial fixed-field claims.
  Existing `TryDecode` APIs remain compatibility adapters. ACK encoding also
  returns `Either`; other invalid construction and native crypto failures can
  still throw.
- Packet limits: 255-byte packet, 64-byte path, 184-byte payload, at most 63 path
  entries. Decode preserves future versions and reserved width-four paths; inspect
  support flags before interpretation. TRACE path bytes are not ordinary hops.
- Parsing ADVERTs does not verify signatures. A valid signature does not prove freshness,
  location accuracy, outer-path integrity, or authenticated traversal.
- App-data fields are unverified claims. Partial decoding can retain usable fields;
  keep the original bytes because partial fields cannot reconstruct them.
- Dispose signing/channel keys. `ImportSeed` takes a 32-byte Ed25519 seed, not
  arbitrary firmware key serialization; callers protect and clear their inputs.
- Channel secrets are 16 or 32 bytes. The two-byte MAC provides no sender identity
  or replay protection; a channel hash is only a lookup hint.
- Group text supports attempt bits 0–3 and up to 171 UTF-8 bytes, further limited
  by packet space. Encoding rejects embedded NUL; decoding follows wire NUL
  termination.
- ACK bytes do not prove acknowledgment. Multipart/control decoding does not
  validate inner bodies, perform assembly, or imply forwarding.

## Tests

Tests use independent [ADVERT vectors](../../tests/fixtures/advert-vectors.md) and
frozen group-text vectors. Protocol reference: MeshCore
`81dbae592ffad40b4212bc0b37e82b38da08f4bc`. Firmware execution and installed NuGet
package consumption have not been verified.

```sh
mise exec -- dotnet restore Pulsas.slnx
mise exec -- dotnet test tests/Pulsas.MeshCore.Tests --no-restore
```
