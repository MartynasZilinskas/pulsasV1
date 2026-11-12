using Pulsas.ServiceDefaults;
using Pulsas.Worker.ApiService;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

if (builder.Environment.IsDevelopment())
{
    builder
        .Services.AddOptions<DummyMqttOptions>()
        .BindConfiguration("DummyMqtt")
        .Validate(
            options => options.IsValid(),
            "DummyMqtt requires a WebSocket endpoint and a three-letter region."
        )
        .ValidateOnStart();
    builder.Services.AddSingleton<DummyMqttConsumer>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<DummyMqttConsumer>());
    builder.Services.AddHealthChecks().AddCheck<DummyMqttConsumer>("dummy-mqtt");
}

var app = builder.Build();
app.MapDefaultEndpoints();
await app.RunAsync();
