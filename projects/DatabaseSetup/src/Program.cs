using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Pulsas.Persistence;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("pulsas")
    ?? throw new InvalidOperationException("ConnectionStrings:pulsas is required.");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
await using var database = new TrackingDbContext(
    TrackingDatabase
        .Configure(new DbContextOptionsBuilder<TrackingDbContext>(), connectionString)
        .Options
);
await database.Database.MigrateAsync(cancellation.Token);
Console.WriteLine("Pulsas database migrations applied.");
