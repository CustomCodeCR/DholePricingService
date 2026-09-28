using Dhole.Pricing.Application.MarketPricing.Normalization;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IMarketRateNormalizationService
{
    Task<MarketRateNormalizationResult> NormalizeAsync(
        MarketRateNormalizationRequest request,
        CancellationToken cancellationToken = default
    );
}
