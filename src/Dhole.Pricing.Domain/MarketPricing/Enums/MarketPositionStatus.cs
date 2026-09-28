namespace Dhole.Pricing.Domain.MarketPricing.Enums;

public enum MarketPositionStatus
{
    InsufficientMarketData = 0,
    BelowMinimumMargin = 1,
    BelowCompetitiveRange = 2,
    Competitive = 3,
    AboveCompetitiveRange = 4,
}
