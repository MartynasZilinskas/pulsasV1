using Pulsas.ServiceDefaults;
using Pulsas.TrafficGenerator;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder
    .Services.AddOptions<TrafficGeneratorOptions>()
    .Bind(builder.Configuration.GetSection("TrafficGenerator"))
    .Validate(options => options.IsValid(), "Invalid TrafficGenerator configuration.")
    .ValidateOnStart();
builder.Services.AddSingleton<TrafficGeneratorManager>();
builder.Services.AddSingleton<ITrafficGeneratorManager>(services =>
    services.GetRequiredService<TrafficGeneratorManager>()
);
builder.Services.AddHostedService(services =>
    services.GetRequiredService<TrafficGeneratorManager>()
);
var app = builder.Build();
app.MapDefaultEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapGet("/traffic/status", (ITrafficGeneratorManager manager) => manager.Status);
    app.MapPost(
        "/traffic/start",
        async (ITrafficGeneratorManager manager, CancellationToken cancellationToken) =>
            Results.Ok(await manager.StartAsync(cancellationToken))
    );
    app.MapPost(
        "/traffic/stop",
        async (ITrafficGeneratorManager manager, CancellationToken cancellationToken) =>
            Results.Ok(await manager.StopAsync(cancellationToken))
    );
}
await app.RunAsync();
