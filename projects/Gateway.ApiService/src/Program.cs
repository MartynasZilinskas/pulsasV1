using ChilliCream.Nitro.App;
using Pulsas.Gateway.ApiService;
using Pulsas.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddGraphQLServer().AddQueryType<Query>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGraphQLHttp("/graphql");
if (app.Environment.IsDevelopment())
{
    app.MapNitroApp("/graphql/ui")
        .WithOptions(options =>
        {
            options.GraphQLEndpoint = "/graphql";
            options.UseGet = false;
            options.DisableTelemetry = true;
            options.ServeMode = ServeMode.Embedded;
            options.Document = "query { serviceInfo { name role } }";
        });
}

app.Logger.LogInformation(
    "Gateway foundation started; only operational service metadata is exposed."
);
await app.RunAsync();
