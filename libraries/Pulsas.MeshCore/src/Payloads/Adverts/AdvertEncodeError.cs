namespace Pulsas.MeshCore.Payloads.Adverts;

public record AdvertEncodeError(AdvertEncodeErrorKind Kind, int? Length = null);
