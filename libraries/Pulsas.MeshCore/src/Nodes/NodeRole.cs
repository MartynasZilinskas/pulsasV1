namespace Pulsas.MeshCore.Nodes;

public enum NodeRole : byte
{
    // Also called Chat or Companion in MeshCore terminology.
    Client = 1,
    Repeater = 2,
    RoomServer = 3,
    Sensor = 4,
}
