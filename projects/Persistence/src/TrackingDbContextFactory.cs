using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pulsas.Persistence;

// Scaffolding needs the provider/model, not a running database or its credentials.
public class TrackingDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args) =>
        new(
            TrackingDatabase
                .Configure(
                    new DbContextOptionsBuilder<TrackingDbContext>(),
                    "Host=127.0.0.1;Port=1;Database=design_only;Username=design_only"
                )
                .Options
        );
}
