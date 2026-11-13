namespace Pulsas.MeshCore.Payloads.Adverts;

public enum AdvertSignatureErrorKind
{
    Unknown = 0,
    UninitializedPublicKey = 1,
    InvalidPublicKeyLength = 2,
    UninitializedSignature = 3,
    InvalidSignatureLength = 4,
    UninitializedAppData = 5,
    InvalidAppDataLength = 6,
    PublicKeyImportRejected = 7,
    SignatureMismatch = 8,
}
