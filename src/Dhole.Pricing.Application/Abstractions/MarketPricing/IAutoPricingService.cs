using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IAutoPricingService
{
    Task<AutoPricingProposal> CalculateAsync(
        AutoPricingRequest request,
        CancellationToken cancellationToken = default
    );

    AutoPricingApplyResult Apply(
        RateHeader rate,
        AutoPricingProposal proposal,
        Guid? updatedBy = null
    );
}
