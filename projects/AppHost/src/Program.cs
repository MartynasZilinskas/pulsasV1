using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Aspire generates the password once and retains it in local user secrets.
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("postgis/postgis")
    .WithImageTag("18-3.6")
    .WithDataVolume("pulsas-v1-postgres-data");
if (builder.ExecutionContext.IsRunMode)
{
    postgres.WithDockerfile("containers/postgres", "Containerfile");
}
var database = postgres.AddDatabase("pulsas", "pulsas_dev");
var databaseSetup = builder
    .AddProject<Projects.Pulsas_DatabaseSetup>("database-setup")
    .WithReference(database)
    .WaitFor(database);

if (builder.ExecutionContext.IsRunMode && builder.Configuration.GetValue<bool>("GeographyTests"))
{
    builder
        .AddExecutable(
            "geography-tests",
            "mise",
            "../..",
            "exec",
            "--",
            "dotnet",
            "test",
            "tests/Pulsas.Worker.Tests",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~GeographyIntegrationTests"
        )
        .WithEnvironment(
            "PULSAS_GEOGRAPHY_TEST_CONNECTION",
            database.Resource.ConnectionStringExpression
        )
        .WaitForCompletion(databaseSetup);
}

// No database references until a service actually consumes persistence.
var worker = builder
    .AddProject<Projects.Pulsas_Worker_ApiService>("worker")
    .WithHttpHealthCheck("/health");
builder.AddProject<Projects.Pulsas_Gateway_ApiService>("gateway").WithHttpHealthCheck("/health");

if (builder.ExecutionContext.IsRunMode)
{
    var mqtt = builder
        .AddContainer("mqtt", "docker.io/library/eclipse-mosquitto", "2.0.22")
        .WithBindMount(
            "configuration/mosquitto.conf",
            "/mosquitto/config/mosquitto.conf",
            isReadOnly: true
        )
        .WithEndpoint(port: 9001, targetPort: 9001, name: "ws", scheme: "ws");

    worker
        .WithEnvironment(
            "DummyMqtt__Endpoint",
            ReferenceExpression.Create($"{mqtt.GetEndpoint("ws")}/mqtt")
        )
        .WaitFor(mqtt);

    builder
        .AddProject<Projects.Pulsas_TrafficGenerator>("traffic-generator")
        .WithEnvironment(
            "TrafficGenerator__Endpoint",
            ReferenceExpression.Create($"{mqtt.GetEndpoint("ws")}/mqtt")
        )
        .WithHttpHealthCheck("/health")
        .WaitFor(mqtt)
        .WithHttpCommand("/traffic/start", "Start traffic", commandName: "start-traffic")
        .WithHttpCommand("/traffic/stop", "Stop traffic", commandName: "stop-traffic");

    builder
        .AddContainer("drizzle-studio", "ghcr.io/drizzle-team/gateway", "1.7.0")
        .WithVolume("pulsas-v1-drizzle-data", "/app")
        .WithEnvironment("STORE_PATH", "/app")
        .WithEnvironment("DATABASE_URL_pulsas", database.Resource.UriExpression)
        .WithHttpEndpoint(port: 4983, targetPort: 4983)
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(databaseSetup);
}

await builder.Build().RunAsync();
