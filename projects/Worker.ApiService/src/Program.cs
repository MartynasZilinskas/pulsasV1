using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.Configuration;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.Persistence;
using Pulsas.Persistence.Nodes;
using Pulsas.ServiceDefaults;
using Pulsas.Worker.ApiService;
using Pulsas.Worker.ApiService.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSecretJsonFiles(Environment.GetEnvironmentVariable("PULSAS_SECRETS_PATH"));
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);
builder.Services.AddSingleton(PacketConfiguration.Read(builder.Configuration));
builder.AddServiceDefaults();

if (builder.Environment.IsDevelopment())
{
    var connectionString = builder.Configuration.GetConnectionString("pulsas")
        ?? throw new InvalidOperationException("ConnectionStrings:pulsas is required.");
    builder.Services.AddDbContextFactory<TrackingDbContext>(options =>
        TrackingDatabase.Configure(options, connectionString)
    );
    builder.Services.AddSingleton<IClock>(SystemClock.Instance);
    builder.Services.AddSingleton<IAdvertNodeManager, AdvertNodeManager>();
    builder.Services.AddSingleton<IPacketProcessingManager, PacketProcessingManager>();
    builder.Services.AddSingleton<MqttPacketReportHandler>();
    builder
        .Services.AddOptions<DummyMqttOptions>()
        .BindConfiguration("DummyMqtt")
        .Validate(
            options => options.IsValid(),
            "DummyMqtt requires a WebSocket endpoint and a region safe for one MQTT topic segment."
        )
        .ValidateOnStart();
    builder.Services.AddSingleton<DummyMqttConsumer>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<DummyMqttConsumer>());
    builder.Services.AddHealthChecks().AddCheck<DummyMqttConsumer>("dummy-mqtt");
}

var app = builder.Build();
app.MapDefaultEndpoints();
await app.RunAsync();
