namespace Pulsas.MeshCore.Payloads.GroupText;

public enum GroupTextDecodeStatus
{
    NotChecked = 0,
    Success = 1,
    Malformed = 2,
    ChannelMismatch = 3,
    MacMismatch = 4,
    UnsupportedTextType = 5,
}
