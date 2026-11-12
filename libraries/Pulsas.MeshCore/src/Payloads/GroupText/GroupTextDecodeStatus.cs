namespace Pulsas.MeshCore.Payloads.GroupText;

public enum GroupTextDecodeStatus
{
    Success,
    Malformed,
    ChannelMismatch,
    MacMismatch,
    UnsupportedTextType,
}
