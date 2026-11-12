namespace Pulsas.MeshCore.Payloads.GroupText;

public record GroupTextDecodeResult(GroupTextDecodeStatus Status, GroupTextMessage? Message = null);
