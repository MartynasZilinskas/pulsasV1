using System.Text.Json;
using Pulsas.PacketProcessing.Contracts.Paths;
using Xunit;

namespace Pulsas.PacketProcessing.Tests;

public class PathAssessmentSerializationTests
{
    [Theory]
    [InlineData(PathHopOutcome.Unknown, "Unknown")]
    [InlineData(PathHopOutcome.Resolved, "Resolved")]
    [InlineData(PathHopOutcome.Unresolved, "Unresolved")]
    [InlineData(PathHopOutcome.Ambiguous, "Ambiguous")]
    public void Hop_outcomes_preserve_existing_JSON_strings(
        PathHopOutcome outcome,
        string storedValue
    )
    {
        var assessment = new PathAssessmentResult(
            PathAssessmentKind.FloodHistory,
            PathAssessmentStatus.Unresolved,
            [new(0, 1, "ab", outcome, [])]
        );

        // Persistence serializes the whole result with default System.Text.Json options.
        var json = JsonSerializer.Serialize(assessment);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            storedValue,
            document.RootElement.GetProperty("Hops")[0].GetProperty("Outcome").GetString()
        );

        var existingJson = $$"""
            {"PathKind":"FloodHistory","Status":"Unresolved","Hops":[{"Position":0,"Width":1,"Prefix":"ab","Outcome":"{{storedValue}}","Candidates":[]}]}
            """;
        var restored = JsonSerializer.Deserialize<PathAssessmentResult>(existingJson);
        Assert.NotNull(restored);
        Assert.Equal(outcome, Assert.Single(restored.Hops).Outcome);
    }

    [Theory]
    [InlineData(PathAssessmentStatus.Empty, "Empty")]
    [InlineData(PathAssessmentStatus.Resolved, "Resolved")]
    [InlineData(PathAssessmentStatus.Partial, "Partial")]
    [InlineData(PathAssessmentStatus.Unresolved, "Unresolved")]
    [InlineData(PathAssessmentStatus.UnsupportedVersion, "UnsupportedVersion")]
    [InlineData(PathAssessmentStatus.UnsupportedTraceAssessment, "UnsupportedTraceAssessment")]
    [InlineData(PathAssessmentStatus.UnsupportedWidth, "UnsupportedWidth")]
    [InlineData(PathAssessmentStatus.UnsupportedDirectAssessment, "UnsupportedDirectAssessment")]
    public void Assessment_statuses_preserve_existing_JSON_strings(
        PathAssessmentStatus status,
        string storedValue
    )
    {
        var assessment = new PathAssessmentResult(PathAssessmentKind.FloodHistory, status, []);
        var json = JsonSerializer.Serialize(assessment);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(storedValue, document.RootElement.GetProperty("Status").GetString());

        var existingJson = $$"""
            {"PathKind":"FloodHistory","Status":"{{storedValue}}","Hops":[]}
            """;
        var restored = JsonSerializer.Deserialize<PathAssessmentResult>(existingJson);
        Assert.NotNull(restored);
        Assert.Equal(status, restored.Status);
    }

    [Theory]
    [InlineData(
        PathCandidateConfidence.Weak,
        "Weak",
        PathNeighbourSupportBasis.AdvertToObserver,
        "AdvertToObserver",
        PathNeighbourSupportSide.Incoming,
        "Incoming"
    )]
    [InlineData(
        PathCandidateConfidence.Strong,
        "Strong",
        PathNeighbourSupportBasis.AdvertToUniqueFirstForwarder,
        "AdvertToUniqueFirstForwarder",
        PathNeighbourSupportSide.Outgoing,
        "Outgoing"
    )]
    public void Candidate_and_support_enums_preserve_existing_JSON_strings(
        PathCandidateConfidence confidence,
        string storedConfidence,
        PathNeighbourSupportBasis basis,
        string storedBasis,
        PathNeighbourSupportSide side,
        string storedSide
    )
    {
        PathCandidateReason[] reasons =
        [
            PathCandidateReason.PrefixMatch,
            PathCandidateReason.UniqueKnownPrefixMatch,
            PathCandidateReason.NeighbourSupport,
        ];
        var support = new PathNeighbourSupport(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ab00",
            "cd00",
            basis,
            side
        );
        var candidate = new PathCandidateAssessment(
            Guid.NewGuid(),
            "ab00",
            confidence,
            false,
            [.. reasons],
            [support]
        );
        var assessment = new PathAssessmentResult(
            PathAssessmentKind.FloodHistory,
            PathAssessmentStatus.Unresolved,
            [new(0, 1, "ab", PathHopOutcome.Unresolved, [candidate])]
        );

        var json = JsonSerializer.Serialize(assessment);
        using var document = JsonDocument.Parse(json);
        var storedCandidate = document.RootElement.GetProperty("Hops")[0].GetProperty("Candidates")[
            0
        ];
        Assert.Equal(storedConfidence, storedCandidate.GetProperty("Confidence").GetString());
        Assert.Equal(
            ["PrefixMatch", "UniqueKnownPrefixMatch", "NeighbourSupport"],
            storedCandidate
                .GetProperty("Reasons")
                .EnumerateArray()
                .Select(reason => reason.GetString())
        );
        var storedSupport = storedCandidate.GetProperty("Supports")[0];
        Assert.Equal(storedBasis, storedSupport.GetProperty("Basis").GetString());
        Assert.Equal(storedSide, storedSupport.GetProperty("Side").GetString());

        var existingJson = $$"""
            {"PathKind":"FloodHistory","Status":"Unresolved","Hops":[{"Position":0,"Width":1,"Prefix":"ab","Outcome":"Unresolved","Candidates":[{"NodeId":"{{candidate.NodeId}}","PublicKey":"ab00","Confidence":"{{storedConfidence}}","Selected":false,"Reasons":["PrefixMatch","UniqueKnownPrefixMatch","NeighbourSupport"],"Supports":[{"ObservationId":"{{support.ObservationId}}","SourcePacketId":"{{support.SourcePacketId}}","SourceReceptionId":"{{support.SourceReceptionId}}","SourceContentId":"{{support.SourceContentId}}","FromKey":"ab00","ToKey":"cd00","Basis":"{{storedBasis}}","Side":"{{storedSide}}"}]}]}]}
            """;
        var restored = JsonSerializer.Deserialize<PathAssessmentResult>(existingJson);
        Assert.NotNull(restored);
        var restoredCandidate = Assert.Single(Assert.Single(restored.Hops).Candidates);
        Assert.Equal(confidence, restoredCandidate.Confidence);
        Assert.Equal(reasons, restoredCandidate.Reasons.ToArray());
        Assert.Equal(support, Assert.Single(restoredCandidate.Supports));
    }

    [Theory]
    [InlineData(PathAssessmentKind.TraceProgress, "TraceProgress")]
    [InlineData(PathAssessmentKind.FloodHistory, "FloodHistory")]
    [InlineData(PathAssessmentKind.DirectRemainingRoute, "DirectRemainingRoute")]
    public void Assessment_kinds_preserve_existing_JSON_strings(
        PathAssessmentKind kind,
        string storedValue
    )
    {
        var assessment = new PathAssessmentResult(kind, PathAssessmentStatus.Unresolved, []);
        var json = JsonSerializer.Serialize(assessment);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(storedValue, document.RootElement.GetProperty("PathKind").GetString());

        var existingJson = $$"""
            {"PathKind":"{{storedValue}}","Status":"Unresolved","Hops":[]}
            """;
        var restored = JsonSerializer.Deserialize<PathAssessmentResult>(existingJson);
        Assert.NotNull(restored);
        Assert.Equal(kind, restored.PathKind);
    }
}
