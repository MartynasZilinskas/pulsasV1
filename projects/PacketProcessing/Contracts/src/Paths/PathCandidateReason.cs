using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathCandidateReason>))]
public enum PathCandidateReason
{
    PrefixMatch,
    UniqueKnownPrefixMatch,
    NeighbourSupport,
}
