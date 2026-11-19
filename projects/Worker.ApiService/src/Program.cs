using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pulsas.Configuration;
using Pulsas.PacketProcessing.Contracts.Chat;
using Pulsas.PacketProcessing.Contracts.Ingestion;
using Pulsas.PacketProcessing.Contracts.Nodes;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.PacketProcessing.Contracts.Processing;
using Pulsas.PacketProcessing.Core.Chat;
using Pulsas.PacketProcessing.Core.Ingestion;
using Pulsas.PacketProcessing.Core.Nodes;
using Pulsas.PacketProcessing.Core.Paths;
using Pulsas.PacketProcessing.Core.Processing;
using Pulsas.Persistence;
using Pulsas.ServiceDefaults;
using Pulsas.Worker.ApiService;
using Pulsas.Worker.ApiService.Chat;
using Pulsas.Worker.ApiService.Configuration;
using Pulsas.Worker.ApiService.Delivery;

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
    builder.Services.AddScoped<IAdvertNodeManager, AdvertNodeManager>();
    builder.Services.AddScoped<IPathAssessmentManager, PathAssessmentManager>();
    builder.Services.AddSingleton<IPacketProcessingManager, PacketProcessingManager>();
    builder.Services.AddSingleton<IReportReceiver, ReportReceiver>();
    builder.Services.AddScoped<IReportIngestionManager, ReportIngestionManager>();
    builder.Services.AddScoped<IReportQueueProcessor, ReportQueueProcessor>();
    builder.Services.AddScoped<IGroupTextManager>(services => new GroupTextManager(
        services.GetRequiredService<TrackingDbContext>(),
        services.GetRequiredService<PacketConfiguration>().Channels,
        services.GetRequiredService<IClock>()
    ));
    builder.Services.AddScoped<IChannelCatalogueManager>(services => new ChannelCatalogueManager(
        services.GetRequiredService<TrackingDbContext>(),
        services.GetRequiredService<PacketConfiguration>().Channels,
        services.GetRequiredService<IClock>()
    ));
    builder
        .Services.AddOptions<ReportDeliveryOptions>()
        .Configure<IConfiguration>(
            (options, configuration) =>
            {
                var delivery = configuration.GetSection("ReportDelivery");
                options.MaximumAttempts = delivery.GetValue(
                    nameof(options.MaximumAttempts),
                    options.MaximumAttempts
                );
                options.RetryDelaySeconds = delivery.GetValue(
                    nameof(options.RetryDelaySeconds),
                    options.RetryDelaySeconds
                );
                options.PollDelayMilliseconds = delivery.GetValue(
                    nameof(options.PollDelayMilliseconds),
                    options.PollDelayMilliseconds
                );
                options.ContentConcurrency = delivery.GetValue(
                    nameof(options.ContentConcurrency),
                    options.ContentConcurrency
                );
            }
        )
        .Validate(
            options => options.IsValid(),
            "ReportDelivery requires distinct valid queue names, positive attempt/poll/concurrency limits and a nonnegative retry delay."
        )
        .ValidateOnStart();
    builder.Services.AddHostedService<ChannelCatalogueInitializer>();
    builder.Services.AddHostedService<ReportQueueConsumer>();
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
