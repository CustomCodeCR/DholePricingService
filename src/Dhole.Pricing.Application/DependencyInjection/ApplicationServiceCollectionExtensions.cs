using CustomCodeFramework.Cqrs.DependencyInjection;
using CustomCodeFramework.Validation.DependencyInjection;
using Dhole.Pricing.Application.Abstractions.Services;
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

        return services;
    }
}
