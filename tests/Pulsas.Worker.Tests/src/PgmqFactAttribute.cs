using Xunit;

namespace Pulsas.Worker.Tests;

public class PgmqFactAttribute : FactAttribute
{
    public PgmqFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PULSAS_PGMQ_TEST_PORT") is null)
        {
            Skip = "Requires an explicitly provisioned disposable PostgreSQL; see README.";
        }
    }
}
