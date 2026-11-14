namespace Pulsas.MeshCore.Payloads.GroupText;

public enum GroupTextDecodeErrorKind
{
    Unknown = 0,
    Malformed = 1,
    ChannelMismatch = 2,
    MacMismatch = 3,
    UnsupportedTextType = 4,
}
