using Dhole.Pricing.Domain.MarketPricing.Enums;

namespace Dhole.Pricing.Domain.MarketPricing.Models;

public sealed record PricingMarketPosition(
    decimal Cost,
    decimal OriginalSale,
    decimal MinimumSalePrice,
    decimal? MarketMedian,
    decimal? WeightedMarketAverage,
    decimal? TargetMarketPrice,
    decimal? CompetitiveCeiling,
    decimal SuggestedSalePrice,
    decimal CurrentMargin,
    decimal SuggestedMargin,
    decimal AvailableHeadroom,
    decimal ConfidenceScore,
    MarketPositionStatus Status
);
