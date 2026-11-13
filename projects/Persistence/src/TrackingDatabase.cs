using Microsoft.EntityFrameworkCore;

namespace Pulsas.Persistence;

public static class TrackingDatabase
{
    public static DbContextOptionsBuilder<TrackingDbContext> Configure(
        DbContextOptionsBuilder<TrackingDbContext> builder,
        string connectionString
    ) => builder.UseNpgsql(connectionString, options =>
        {
            options.UseNodaTime();
            options.UseNetTopologySuite();
            PostgresEnums.Configure(options);
        });
}
