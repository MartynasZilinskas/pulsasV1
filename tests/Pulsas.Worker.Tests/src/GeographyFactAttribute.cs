using Xunit;

namespace Pulsas.Worker.Tests;

public class GeographyFactAttribute : FactAttribute
{
    public GeographyFactAttribute()
    {
        if (
            string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("PULSAS_GEOGRAPHY_TEST_CONNECTION")
            )
        )
        {
            Skip = "Set PULSAS_GEOGRAPHY_TEST_CONNECTION to the migrated local Aspire database.";
        }
    }
}
