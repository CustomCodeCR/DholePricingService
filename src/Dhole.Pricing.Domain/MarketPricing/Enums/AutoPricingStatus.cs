namespace Dhole.Pricing.Domain.MarketPricing.Enums;

public enum AutoPricingStatus
{
    NotCalculated = 0,
    Calculated = 1,
    AutoApplied = 2,
    NeedsReview = 3,
    InsufficientMarketData = 4,
    AboveMarket = 5,
    BelowMinimumMargin = 6,
    ManuallyAdjusted = 7,
    Approved = 8,
    Rejected = 9,
}
