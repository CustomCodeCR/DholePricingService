using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.Comparability;

public enum MarketCarrierMarketKind
{
    Primary = 1,
    Secondary = 2,
    Historical = 3,
}

public enum MarketConfidenceLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
}

public sealed record MarketComparabilityDimensionScores(
    decimal Incoterm,
    decimal Route,
    decimal Equipment,
    decimal Mode,
    decimal Carrier,
    decimal Recency,
    decimal Extraction,
    decimal Normalization,
    decimal Completeness
);

public sealed record ComparableMarketObservation(
    CompetitorRateObservation Observation,
    MarketComparabilityDimensionScores Scores,
    decimal StructuralComparabilityWeight,
    decimal ComparabilityScore,
    decimal RecencyWeight,
    decimal FinalWeight,
    MarketCarrierMarketKind CarrierMarket,
    bool IsExactMatch,
    bool WasIncluded,
    string? ExclusionReason
)
{
    public bool IsPrimaryCarrierMarket => CarrierMarket == MarketCarrierMarketKind.Primary;
}

public sealed record MarketConfidenceBreakdown(
    decimal CompetitorFactor,
    decimal ObservationFactor,
    decimal ExactMatchRatio,
    decimal CarrierExactMatchRatio,
    decimal AverageCompleteness,
    decimal AverageExtractionConfidence,
    decimal AverageNormalizationConfidence,
    decimal AverageRecencyWeight,
    decimal PrimaryCarrierAvailability,
    decimal Score
);

public sealed record MarketComparabilityResult(
    MarketComparisonKey Key,
    DateTime ReferenceDate,
    int CandidateCount,
    int IncludedObservationCount,
    int CompetitorCount,
    int ExactMatchCount,
    int CarrierExactMatchCount,
    bool CarrierFallbackUsed,
    bool HasSufficientMarketData,
    decimal ConfidenceScore,
    MarketConfidenceLevel ConfidenceLevel,
    MarketConfidenceBreakdown ConfidenceBreakdown,
    string AlgorithmVersion,
    IReadOnlyCollection<ComparableMarketObservation> Observations
);
