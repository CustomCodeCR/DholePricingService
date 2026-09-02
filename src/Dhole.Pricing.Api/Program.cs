using CustomCodeFramework.Api.DependencyInjection;
using CustomCodeFramework.Api.Swagger;
using CustomCodeFramework.Core.Abstractions;
using Dhole.Pricing.Api.Endpoints;
//using Dhole.Pricing.Api.Grpc;
using Dhole.Pricing.Api.Middleware;
using Dhole.Pricing.Application.DependencyInjection;
using Dhole.Pricing.Infrastructure.DependencyInjection;
using Dhole.Pricing.Infrastructure.Time;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "DholeWebCors";

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddCustomCodeApiWithSwagger(title: "Dhole Pricing Service", version: "v1");

var configuredOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? [];
var allowedOrigins = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
    "http://192.168.1.193:5173",
    "https://sistema.logisticacastrofallas.com",
    "https://dhole.customcodecr.com",
}
.Concat(configuredOrigins)
.Distinct(StringComparer.OrdinalIgnoreCase)
.ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    );
});

builder.Services.AddGrpc();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpClient("DholeAI", client =>
{
    var baseAddress = builder.Configuration["AI:Client:BaseAddress"] ?? "http://ai-api:5206/";
    if (!baseAddress.EndsWith('/'))
    {
        baseAddress += "/";
    }

    client.BaseAddress = new Uri(baseAddress);
    client.Timeout = TimeSpan.FromSeconds(
        int.TryParse(builder.Configuration["AI:Client:TimeoutSeconds"], out var timeoutSeconds)
        && timeoutSeconds > 0
            ? timeoutSeconds
            : 180
    );
});

var app = builder.Build();

app.UseCustomCodeApi();

app.UseCors(CorsPolicyName);

if (app.Environment.IsDevelopment())
{
    app.UseCustomCodeSwagger();
}

app.MapGet(
        "/health",
        () =>
        {
            return Results.Ok(
                new
                {
                    service = "DholePricingService",
                    status = "Healthy",
                    timestamp = DateTimeOffset.UtcNow,
                }
            );
        }
    )
    .AllowAnonymous();

app.UseAuthentication();
app.UseMiddleware<AuditExecutionContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AuditEndpointMiddleware>();

//app.MapGrpcService<ConfigCatalogGrpcService>();

app.MapCostEndpoints();
app.MapImportRateEndpoints();
app.MapImportRateReviewQueueEndpoints();
app.MapCabysEndpoints();
app.MapRateEndpoints();
app.MapRateTermItemEndpoints();
app.MapPricingRuleConfigurationEndpoints();
app.MapCommercialTermEndpoints();
app.MapPricingConfigCatalogEndpoints();
app.MapDataExtractionImportEndpoints();
app.MapLogisticsNewsEndpoints();
app.MapLclEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();

    await dbContext.Database.MigrateAsync();
}

app.Run();