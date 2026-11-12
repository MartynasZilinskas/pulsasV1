using Xunit;

namespace Pulsas.TrafficGenerator.Tests;

public class LocalBrokerFactAttribute : FactAttribute
{
    public LocalBrokerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PULSAS_LOCAL_MQTT_TESTS") != "1")
        {
            Skip = "Requires explicit opt-in and the local Aspire stack; see README.";
        }
    }
}
