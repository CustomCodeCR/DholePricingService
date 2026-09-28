using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IMarketRateNormalizationStrategy
{
    bool CanHandle(ShipmentMode mode);

    MarketStrategyNormalizationResult Normalize(MarketStrategyNormalizationContext context);
}
