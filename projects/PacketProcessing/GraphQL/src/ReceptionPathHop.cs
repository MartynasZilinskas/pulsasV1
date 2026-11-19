using HotChocolate.CostAnalysis.Types;
using Pulsas.PacketProcessing.Contracts.Paths;

namespace Pulsas.PacketProcessing.GraphQL;

public class ReceptionPathHop(PathHopAssessment hop)
{
    private const int MaximumPageSize = 20;
    private const int DefaultPageSize = 8;

    public int Position => hop.Position;
    public int Width => hop.Width;
    public string Prefix => hop.Prefix;
    public PathHopOutcome Outcome => hop.Outcome;

    public ReceptionPathCandidate? SelectedCandidate =>
        hop.Candidates.FirstOrDefault(candidate => candidate.Selected) is { } selected
            ? ReceptionPathCandidate.From(selected)
            : null;

    [ListSize(
        AssumedSize = MaximumPageSize,
        SlicingArguments = ["first"],
        SlicingArgumentDefaultValue = DefaultPageSize,
        SizedFields = ["items"],
        RequireOneSlicingArgument = false
    )]
    public ReceptionPathCandidatePage GetCandidates(int first = DefaultPageSize, int offset = 0)
    {
        if (first is < 1 or > MaximumPageSize || offset < 0)
        {
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"Expected first between 1 and {MaximumPageSize}, and a nonnegative offset."
                    )
                    .SetCode("INVALID_INPUT")
                    .Build()
            );
        }

        return new(
            [.. hop.Candidates.Skip(offset).Take(first).Select(ReceptionPathCandidate.From)],
            hop.Candidates.Length,
            hop.Candidates.Length - offset > first
        );
    }
}
