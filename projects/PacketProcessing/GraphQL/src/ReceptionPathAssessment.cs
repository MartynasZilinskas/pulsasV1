using System.Collections.Immutable;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.MeshCore.Packets;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class ReceptionPathAssessment(ReadPathAssessmentResult row)
{
    public Instant AssessedAt => row.AssessedAt;
    public string PolicyVersion => row.PolicyVersion;
    public PathAssessmentKind PathKind => row.Result.PathKind;
    public PathAssessmentStatus Status => row.Result.Status;

    [ListSize(AssumedSize = PacketCodec.MaximumPathLength)]
    public ImmutableArray<ReceptionPathHop> Hops =>
        row.Result.Hops.Select(hop => new ReceptionPathHop(hop)).ToImmutableArray();
}
