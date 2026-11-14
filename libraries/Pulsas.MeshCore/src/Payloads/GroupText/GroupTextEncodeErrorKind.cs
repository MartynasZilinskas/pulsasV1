namespace Pulsas.MeshCore.Payloads.GroupText;

public enum GroupTextEncodeErrorKind
{
    Unknown = 0,
    InvalidAttempt = 1,
    EmbeddedNul = 2,
    InvalidText = 3,
    InvalidTextLength = 4,
}
