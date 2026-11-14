namespace Pulsas.MeshCore.Payloads.Control;

public record ControlEncodeError(ControlEncodeErrorKind Kind, int? Length = null);
