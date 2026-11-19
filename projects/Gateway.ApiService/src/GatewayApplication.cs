using System.Net;
using ChilliCream.Nitro.App;
using HotChocolate.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;
using NodaTime;
using Npgsql;
using Pulsas.PacketProcessing.Contracts.Paths;
using Pulsas.PacketProcessing.GraphQL;
using Pulsas.Persistence;
using Pulsas.Persistence.Paths;
using Pulsas.ServiceDefaults;

namespace Pulsas.Gateway.ApiService;

public static class GatewayApplication
{
    private const int MaximumRequestBytes = 16 * 1024;

    public static WebApplication Build(
        WebApplicationOptions options,
        Action<WebApplicationBuilder>? configure = null
    )
    {
        var builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);
        builder.AddServiceDefaults();
        var readsEnabled =
            builder.Environment.IsDevelopment()
            && builder.Configuration.GetValue<bool>("Gateway:EnableReceptionReads");
        if (readsEnabled)
        {
            builder.WebHost.ConfigureKestrel(options =>
                options.Limits.MaxRequestBodySize = MaximumRequestBytes
            );
            var connection = new NpgsqlConnectionStringBuilder(
                builder.Configuration.GetConnectionString("pulsas")
                    ?? throw new InvalidOperationException(
                        "ConnectionStrings:pulsas is required for reception reads."
                    )
            )
            {
                CommandTimeout = 2,
                IncludeErrorDetail = false,
                MaxPoolSize = 4,
            };
            builder.Services.AddDbContextFactory<TrackingDbContext>(options =>
                TrackingDatabase.Configure(options, connection.ConnectionString)
            );
            builder.Services.AddSingleton<ReceptionReadManager>();
            builder.Services.TryAddSingleton<IReceptionReadManager>(services =>
                services.GetRequiredService<ReceptionReadManager>()
            );
            builder.Services.TryAddSingleton<IReceptionBrowserManager>(services =>
                services.GetRequiredService<ReceptionReadManager>()
            );
            builder.Services.TryAddSingleton<IClock>(SystemClock.Instance);
            builder.Services.TryAddSingleton<
                IMeshPacketPayloadReadManager,
                MeshPacketPayloadReadManager
            >();
            builder.Services.AddSingleton<ReceptionBrowserCursor>();
            builder
                .Services.AddGraphQLServer()
                .AddReceptionBrowser()
                .ConfigureValidation(
                    (_, validator) =>
                        validator.AddRule((_, _) => new RootReadLimitRule(), (_, _) => true)
                )
                .BindRuntimeType<Instant, InstantType>()
                .ModifyRequestOptions(options =>
                {
                    options.ExecutionTimeout = TimeSpan.FromSeconds(3);
                    options.IncludeExceptionDetails = false;
                })
                .ModifyCostOptions(options =>
                {
                    options.EnforceCostLimits = true;
                    options.MaxFieldCost = 36_000;
                    options.MaxTypeCost = 12_000;
                });
        }

        var app = builder.Build();
        app.MapDefaultEndpoints();
        if (readsEnabled)
        {
            app.Use(
                async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/graphql"))
                    {
                        if (
                            context.Connection.RemoteIpAddress is not { } remote
                            || !IPAddress.IsLoopback(remote)
                            || !IsLoopbackHost(context.Request.Host.Host)
                        )
                        {
                            context.Response.StatusCode = StatusCodes.Status404NotFound;
                            return;
                        }
                        if (!HasLocalOrigin(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return;
                        }

                        if (!context.Request.Path.StartsWithSegments("/graphql/ui"))
                        {
                            if (
                                !HttpMethods.IsPost(context.Request.Method)
                                || context.Request.QueryString.HasValue
                            )
                            {
                                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                                return;
                            }
                            if (!context.Request.HasJsonContentType())
                            {
                                context.Response.StatusCode =
                                    StatusCodes.Status415UnsupportedMediaType;
                                return;
                            }
                            if (context.Request.ContentLength > MaximumRequestBytes)
                            {
                                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                                return;
                            }
                        }
                    }
                    await next(context);
                }
            );
            app.MapGraphQLHttp("/graphql")
                .WithOptions(options =>
                {
                    options.EnableGetRequests = false;
                    options.EnableMultipartRequests = false;
                    options.Batching = AllowedBatching.None;
                });
            app.MapNitroApp("/graphql/ui")
                .WithOptions(options =>
                {
                    options.GraphQLEndpoint = "/graphql";
                    options.UseGet = false;
                    options.DisableTelemetry = true;
                    options.ServeMode = ServeMode.Embedded;
                });
        }

        app.Logger.LogInformation(
            "Gateway started; development reception reads: {Enabled}.",
            readsEnabled
        );
        return app;
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host is "127.0.0.1" or "[::1]";

    private static bool HasLocalOrigin(HttpRequest request) =>
        !request.Headers.TryGetValue(HeaderNames.Origin, out var origins)
        || (
            origins.Count == 1
            && string.Equals(
                origins[0],
                $"{request.Scheme}://{request.Host.Value}",
                StringComparison.OrdinalIgnoreCase
            )
        );
}
