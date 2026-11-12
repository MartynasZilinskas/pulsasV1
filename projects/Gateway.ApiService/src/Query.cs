namespace Pulsas.Gateway.ApiService;

public class Query
{
    [GraphQLDescription(
        "Operational scaffold metadata only; not domain functionality or dependency readiness."
    )]
    public ServiceInfo GetServiceInfo() => new("gateway", "operational scaffold");
}
