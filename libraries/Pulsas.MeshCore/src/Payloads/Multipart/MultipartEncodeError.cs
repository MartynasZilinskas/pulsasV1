namespace Pulsas.MeshCore.Payloads.Multipart;

public record MultipartEncodeError(MultipartEncodeErrorKind Kind, int? Length = null);
