using System.Collections.Immutable;
using Funcky;

namespace Pulsas.Worker.ApiService.Delivery;

public interface IReportReceiver
{
    record ReceiveReportInput(
        Guid ReceptionId,
        ImmutableArray<byte> OriginalReport,
        string Topic,
        string BrokerEndpoint,
        DateTimeOffset ArrivedAt
    );

    // Success means durably queued, not interpreted or projected.
    Task<Unit> ReceiveReportAsync(ReceiveReportInput input, CancellationToken cancellationToken);
}
