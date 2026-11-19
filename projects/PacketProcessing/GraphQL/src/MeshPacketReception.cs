using Funcky.Monads;
using HotChocolate.CostAnalysis.Types;
using NodaTime;
using Pulsas.PacketProcessing.Contracts.Paths;
using static Pulsas.PacketProcessing.Contracts.Paths.IReceptionReadManager;

namespace Pulsas.PacketProcessing.GraphQL;

public class MeshPacketReception(ReadReceptionResult row)
{
    [GraphQLType(typeof(NonNullType<IdType>))]
    public Guid Id => row.Id;
    public Instant ArrivedAt => row.ArrivedAt;
    public Instant? HeardAt => row.HeardAt;
    public int? Rssi => row.Rssi;
    public double? Snr => row.Snr;

    [GraphQLType(typeof(IdType))]
    public Guid? PacketId => row.PacketId;

    public Broker? Broker => row.Details?.Broker is { } broker ? new(broker.Id, broker.Name) : null;

    public Region? Region => row.Details?.Region is { } region ? Region.From(region) : null;

    public Observer? GetObserver([Service] IClock clock) =>
        row.Details?.Observer is { } observer
            ? Observer.From(observer, clock.GetCurrentInstant())
            : null;

    public IPacketResult Packet => MeshPacket.From(row.Details?.Packet);

    [Cost(15000)]
    [GraphQLDescription("The stored point-in-time result, or null if no assessment was recorded.")]
    public async Task<ReceptionPathAssessment?> GetPathAssessmentAsync(
        [Service] IReceptionReadManager reads,
        CancellationToken cancellationToken
    )
    {
        var assessment = await reads.ReadPathAssessmentAsync(Id, cancellationToken);
        return assessment.Match(
            left: _ =>
                throw new GraphQLException(
                    ErrorBuilder
                        .New()
                        .SetMessage("The stored path assessment exceeds the Gateway read limit.")
                        .SetCode("PATH_ASSESSMENT_TOO_LARGE")
                        .Build()
                ),
            right: result => result.Select(value => new ReceptionPathAssessment(value)).ToNullable()
        );
    }
}
