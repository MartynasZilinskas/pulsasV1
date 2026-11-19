using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Pulsas.Persistence.Analytics;

internal static class ArrivalBuckets
{
    [DbFunction("date_bin", IsBuiltIn = true)]
    public static Instant Bin(Duration width, Instant arrival, Instant origin) =>
        throw new NotSupportedException("Arrival bins are evaluated by PostgreSQL.");
}
