using Microsoft.EntityFrameworkCore;

namespace Pulsas.Persistence;

public static class TrackingDatabase
{
    public static DbContextOptionsBuilder<TrackingDbContext> Configure(
        DbContextOptionsBuilder<TrackingDbContext> builder,
        string connectionString
    )
    {
        Configure((DbContextOptionsBuilder)builder, connectionString);
        return builder;
    }

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder builder,
        string connectionString
    ) => builder.UseNpgsql(connectionString, options =>
        {
            options.UseNodaTime();
            options.UseNetTopologySuite();
            PostgresEnums.Configure(options);
        });
}
