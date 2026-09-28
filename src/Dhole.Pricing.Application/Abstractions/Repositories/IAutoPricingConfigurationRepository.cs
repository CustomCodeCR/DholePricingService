using Dhole.Pricing.Domain.MarketPricing.Entities;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IAutoPricingConfigurationRepository
{
    Task<AutoPricingProfile?> GetActiveProfileAsync(
        string? code = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ChargePricingRule>> GetActiveChargeRulesAsync(
        CancellationToken cancellationToken = default
    );
}
