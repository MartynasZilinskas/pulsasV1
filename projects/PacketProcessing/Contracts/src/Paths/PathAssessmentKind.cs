using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathAssessmentKind>))]
public enum PathAssessmentKind
{
    TraceProgress,
    FloodHistory,
    DirectRemainingRoute,
}
