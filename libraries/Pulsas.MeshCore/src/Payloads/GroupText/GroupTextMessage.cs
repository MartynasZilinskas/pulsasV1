namespace Pulsas.MeshCore.Payloads.GroupText;

// Text includes any claimed sender prefix. A shared channel key does not authenticate a sender.
public record GroupTextMessage(uint Timestamp, string Text, byte Attempt = 0);
