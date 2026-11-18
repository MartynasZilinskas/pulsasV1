using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathNeighbourSupportBasis>))]
public enum PathNeighbourSupportBasis
{
    AdvertToObserver,
    AdvertToUniqueFirstForwarder,
}
