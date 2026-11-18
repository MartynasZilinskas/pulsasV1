using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathNeighbourSupportSide>))]
public enum PathNeighbourSupportSide
{
    Incoming,
    Outgoing,
}
