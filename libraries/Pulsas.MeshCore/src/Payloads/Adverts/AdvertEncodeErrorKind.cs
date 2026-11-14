namespace Pulsas.MeshCore.Payloads.Adverts;

public enum AdvertEncodeErrorKind
{
    Unknown = 0,
    UninitializedPublicKey = 1,
    InvalidPublicKeyLength = 2,
    UninitializedSignature = 3,
    InvalidSignatureLength = 4,
    UninitializedAppData = 5,
    InvalidAppDataLength = 6,
}
