using Pulsas.Persistence;

namespace Pulsas.Worker.ApiService.Chat;

// Awaited before queue consumers start; this Worker configuration is the catalogue authority.
public class ChannelCatalogueInitializer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var result = await scope
            .ServiceProvider.GetRequiredService<IChannelCatalogueManager>()
            .ReconcileChannelsAsync(cancellationToken);
        result.Switch(
            left: error =>
                throw new InvalidOperationException(
                    $"Channel catalogue initialization failed: {error}."
                ),
            right: _ => { }
        );
        await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
