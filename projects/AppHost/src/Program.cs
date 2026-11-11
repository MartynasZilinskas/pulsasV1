var builder = DistributedApplication.CreateBuilder(args);

// Aspire generates the password once and retains it in local user secrets.
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("postgis/postgis")
    .WithImageTag("18-3.6")
    .WithDataVolume("pulsas-v1-postgres-data");
var database = postgres.AddDatabase("pulsas", "pulsas_dev");

if (builder.ExecutionContext.IsRunMode)
{
    builder
        .AddContainer("drizzle-studio", "ghcr.io/drizzle-team/gateway", "1.7.0")
        .WithVolume("pulsas-v1-drizzle-data", "/app")
        .WithEnvironment("STORE_PATH", "/app")
        .WithEnvironment("DATABASE_URL_pulsas", database.Resource.UriExpression)
        .WithHttpEndpoint(port: 4983, targetPort: 4983)
        .WithHttpHealthCheck("/health")
        .WaitFor(database);
}

await builder.Build().RunAsync();
