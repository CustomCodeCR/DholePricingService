using CustomCodeFramework.Cqrs.DependencyInjection;
using CustomCodeFramework.Validation.DependencyInjection;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Application.MarketPricing.Persistence;
using Dhole.Pricing.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.Pricing.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddCustomCodeValidation(AssemblyReference.Assembly);

        services.AddCustomCodeCqrs(AssemblyReference.Assembly);
        services.AddCustomCodeCqrsBehaviors();

        services.AddScoped<IRateFixedCostSynchronizer, RateFixedCostSynchronizer>();
        services.AddScoped<IRateExtraDetailResolver, RateExtraDetailResolver>();

        services.AddScoped<MarketCatalogNormalizer>();
        services.AddSingleton<MarketModeNormalizer>();
        services.AddScoped<MarketCurrencyNormalizer>();
        services.AddScoped<MarketChargeNormalizer>();
        services.AddScoped<IMarketRateNormalizationService, MarketRateNormalizationService>();

        services.AddScoped<IMarketRateNormalizationStrategy, FclMarketRateNormalizationStrategy>();
        services.AddScoped<IMarketRateNormalizationStrategy, LclMarketRateNormalizationStrategy>();
        services.AddScoped<IMarketRateNormalizationStrategy, AirMarketRateNormalizationStrategy>();
        services.AddScoped<IMarketRateNormalizationStrategy, FtlMarketRateNormalizationStrategy>();
        services.AddScoped<IMarketRateNormalizationStrategy, LtlMarketRateNormalizationStrategy>();

        services.AddSingleton<MarketComparabilityOptions>();
        services.AddScoped<ICompetitorRateComparabilityService, CompetitorRateComparabilityService>();

        services.AddSingleton<MarketBenchmarkOptions>();
        services.AddScoped<IMarketBenchmarkService, MarketBenchmarkService>();
        services.AddScoped<IAutoPricingService, AutoPricingService>();
        services.AddScoped<IMarketPricingDecisionPersistenceService, MarketPricingDecisionPersistenceService>();
        services.AddScoped<RateMarketPricingContextBuilder>();
        services.AddScoped<IMarketPricingApiWorkflow, MarketPricingApiWorkflow>();

        return services;
    }
}
