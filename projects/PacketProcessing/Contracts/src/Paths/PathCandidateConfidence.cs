using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathCandidateConfidence>))]
public enum PathCandidateConfidence
{
    Weak,
    Strong,
}
