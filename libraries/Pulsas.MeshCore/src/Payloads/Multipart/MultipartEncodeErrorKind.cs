namespace Pulsas.MeshCore.Payloads.Multipart;

public enum MultipartEncodeErrorKind
{
    Unknown = 0,
    InvalidRemaining = 1,
    InvalidPayloadType = 2,
    UninitializedBody = 3,
    InvalidBodyLength = 4,
}
