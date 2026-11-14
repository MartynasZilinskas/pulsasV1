namespace Pulsas.MeshCore.Payloads.Adverts;

// PartialData contains only completely parsed fixed-field claims, not a complete decode.
// Retain original bytes separately; partial claims cannot reconstruct the application data.
public record AdvertAppDataDecodeError(
    AdvertAppDataDecodeErrorKind Kind,
    AdvertAppData? PartialData = null
);
