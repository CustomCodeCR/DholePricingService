using CustomCodeFramework.Core.Abstractions;
using Dhole.Pricing.Application.DependencyInjection;
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

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddPricingWorker(builder.Configuration);

var host = builder.Build();

// El Worker persiste importaciones directamente en PostgreSQL. No puede asumir
// que el API terminó de migrar antes de comenzar a consumir la cola. Aplique las
// migraciones pendientes aquí también; EF/Npgsql serializa la migración y el Worker
// no comenzará a procesar trabajos hasta que su propio modelo y la base estén alineados.
await EnsureDatabaseSchemaAsync(host.Services, builder.Configuration);

await host.RunAsync();

static async Task EnsureDatabaseSchemaAsync(
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
                await dbContext.Database.MigrateAsync();

                var pending = await dbContext.Database.GetPendingMigrationsAsync();
                if (!pending.Any())
                {
                    // Verify the exact column used by extraction imports. This catches
                    // schema drift where EF history says a migration ran but the physical
                    // column is still varchar(2000).
                    var connection = dbContext.Database.GetDbConnection();
                    if (connection.State != System.Data.ConnectionState.Open)
                    {
                        await connection.OpenAsync();
                    }

                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        SELECT data_type, character_maximum_length
                        FROM information_schema.columns
                        WHERE table_schema = 'pricing'
                          AND table_name = 'ImportFclRates'
                          AND column_name = 'space_comment'
                        """;
                    await using var reader = await command.ExecuteReaderAsync();
                    if (
                        await reader.ReadAsync()
                        && string.Equals(
                            reader.GetString(0),
                            "text",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return;
                    }

                    throw new InvalidOperationException(
                        "La columna pricing.ImportFclRates.space_comment no está en tipo text."
                    );
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
        "Pricing Worker no pudo iniciar porque la base de datos no alcanzó el esquema esperado. "
            + "Se intentaron aplicar las migraciones automáticamente antes de consumir trabajos.",
        lastError
    );
}
