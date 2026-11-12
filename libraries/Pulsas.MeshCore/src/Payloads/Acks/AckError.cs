namespace Pulsas.MeshCore.Payloads.Acks;

// An uninitialized immutable checksum has no length, unlike an initialized empty checksum.
public record AckError(AckErrorKind Kind, int? Length = null);
