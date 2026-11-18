using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathHopOutcome>))]
public enum PathHopOutcome
{
    Unknown,
    Resolved,
    Unresolved,
    Ambiguous,
}
