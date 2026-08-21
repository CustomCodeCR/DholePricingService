using CustomCodeFramework.Core.Abstractions;
using Dhole.Pricing.Infrastructure.Time;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.DependencyInjection;
using Dhole.Pricing.Worker.DependencyInjection;
using Dhole.Pricing.Workers.Security;
using Microsoft.EntityFrameworkCore;

var contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "Dhole.Pricing.Workers");

if (!Directory.Exists(contentRoot))
    contentRoot = Directory.GetCurrentDirectory();

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot }
);

builder.Configuration.Sources.Clear();

builder
    .Configuration.SetBasePath(contentRoot)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();

builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddPricingWorker(builder.Configuration);

var host = builder.Build();

// Las migraciones pertenecen al API. El run-dhole inicia API y Worker casi al mismo
// tiempo; ejecutar MigrateAsync en ambos procesos crea una carrera innecesaria y
// aumenta el pico de CPU/memoria durante el arranque. El Worker únicamente espera
// a que el esquema quede listo antes de comenzar a consumir trabajo.
await WaitForDatabaseSchemaAsync(host.Services, builder.Configuration);

await host.RunAsync();

static async Task WaitForDatabaseSchemaAsync(
    IServiceProvider services,
    IConfiguration configuration
)
{
    var timeoutSeconds = Math.Clamp(
        configuration.GetValue("Pricing:WorkerStartup:DatabaseReadyTimeoutSeconds", 90),
        5,
        300
    );
    var retryDelaySeconds = Math.Clamp(
        configuration.GetValue("Pricing:WorkerStartup:DatabaseRetryDelaySeconds", 2),
        1,
        10
    );
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    Exception? lastError = null;

    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();

            if (await dbContext.Database.CanConnectAsync())
            {
                var pending = await dbContext.Database.GetPendingMigrationsAsync();
                if (!pending.Any())
                {
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            lastError = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
    }

    throw new InvalidOperationException(
        "Pricing Worker no pudo iniciar porque la base de datos todavía no está lista. "
            + "Inicie Dhole.Pricing.Api para aplicar las migraciones y vuelva a intentar.",
        lastError
    );
}
