namespace Pulsas.MeshCore.Packets;

public enum PacketType : byte
{
    Request = 0,
    Response = 1,
    Text = 2,
    Ack = 3,
    Advert = 4,
    GroupText = 5,
    GroupData = 6,
    AnonymousRequest = 7,
    Path = 8,
    Trace = 9,
    Multipart = 10,
    Control = 11,
    RawCustom = 15,
}
