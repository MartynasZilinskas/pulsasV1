using Pulsas.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Real MQTT consumers and scheduled tasks will be registered as they migrate.
var app = builder.Build();
app.MapDefaultEndpoints();
app.Logger.LogInformation(
    "Worker foundation started; no MQTT consumers or background tasks are registered."
);
await app.RunAsync();
