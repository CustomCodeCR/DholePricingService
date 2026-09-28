namespace Dhole.Pricing.Domain.MarketPricing.Models;

public sealed record MarketObservationResult(
    Guid CompetitorRateObservationId,
    Guid? CompetitorCompanyId,
    string CompetitorCompanyName,
    decimal? OriginalAmount,
    decimal? NormalizedAmount,
    decimal ComparabilityScore,
    decimal RecencyWeight,
    decimal FinalWeight,
    bool IsPrimaryCarrierMarket,
    bool IsOutlier,
    bool WasIncluded,
    string? ExclusionReason,
    string? OutlierReason
);
