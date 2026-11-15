using Funcky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgmq;
using Npgsql;
using Pulsas.Persistence;
using static Pulsas.Worker.ApiService.Delivery.IReportReceiver;

namespace Pulsas.Worker.ApiService.Delivery;

public class ReportReceiver(
    IDbContextFactory<TrackingDbContext> databases,
    IOptions<ReportDeliveryOptions> options
) : IReportReceiver
{
    public async Task<Unit> ReceiveReportAsync(
        ReceiveReportInput input,
        CancellationToken cancellationToken
    )
    {
        await using var database = await databases.CreateDbContextAsync(cancellationToken);
        await database.Database.OpenConnectionAsync(cancellationToken);
        var queue = new NpgmqClient((NpgsqlConnection)database.Database.GetDbConnection());
        await queue.SendAsync(options.Value.IngestionQueue, input, cancellationToken);
        return Unit.Value;
    }
}
