namespace Pulsas.MeshCore.Payloads.Acks;

public enum AckErrorKind
{
    Unknown = 0,
    InvalidChecksumLength = 1,
    UninitializedChecksum = 2,
}
