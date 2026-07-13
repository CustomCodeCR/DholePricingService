using CustomCodeFramework.Postgres.DependencyInjection;
using CustomCodeFramework.Postgres.EntityFramework.DependencyInjection;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.Auditing;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.Messaging;
using Dhole.Pricing.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.Pricing.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodePostgres(configuration);
        services.AddCustomCodePostgresEntityFramework<ServiceDbContext>();

        services.AddScoped<ICostRepository, CostRepository>();
        services.AddScoped<IImportFclRateRepository, ImportFclRateRepository>();
        services.AddScoped<IRateHeaderRepository, RateHeaderRepository>();

        services.AddScoped<IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();
        services.AddScoped<IPricingAuditService, PricingAuditService>();

        return services;
    }
}
