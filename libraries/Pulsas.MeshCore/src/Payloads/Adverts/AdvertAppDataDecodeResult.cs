namespace Pulsas.MeshCore.Payloads.Adverts;

// Malformed names and unsupported trailing bytes can leave valid fixed-field claims.
// Retain the original bytes separately; partial claims cannot reconstruct that data.
public record AdvertAppDataDecodeResult(
    AdvertAppDataDecodeStatus Status,
    AdvertAppData? Data = null
);
