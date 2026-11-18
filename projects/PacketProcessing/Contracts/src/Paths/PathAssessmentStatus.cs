using System.Text.Json.Serialization;

namespace Pulsas.PacketProcessing.Contracts.Paths;

[JsonConverter(typeof(JsonStringEnumConverter<PathAssessmentStatus>))]
public enum PathAssessmentStatus
{
    Empty,
    Resolved,
    Partial,
    Unresolved,
    UnsupportedVersion,
    UnsupportedTraceAssessment,
    UnsupportedWidth,
    UnsupportedDirectAssessment,
}
