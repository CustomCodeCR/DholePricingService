using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface ICompetitorRateComparabilityService
{
    Task<MarketComparabilityResult> CompareAsync(
        MarketComparisonKey key,
        DateTime referenceDate,
        CancellationToken cancellationToken = default
    );
}
