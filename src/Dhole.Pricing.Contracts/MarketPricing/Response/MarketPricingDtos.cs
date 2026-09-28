namespace Dhole.Pricing.Contracts.MarketPricing.Response;

public sealed record MarketBenchmarkObservationDto(
    Guid CompetitorRateObservationId,
    Guid? CompetitorCompanyId,
    string CompetitorCompanyName,
    Guid? IncotermId,
    string? IncotermCode,
    Guid? PolId,
    string? PolName,
    string? PolCode,
    Guid? PoeId,
    string? PoeName,
    string? PoeCode,
    Guid? PodId,
    string? PodName,
    string? PodCode,
    Guid? ContainerTypeId,
    string? ContainerTypeCode,
    string Mode,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
    DateTime ValidFrom,
    DateTime ValidTo,
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

public sealed record MarketBenchmarkDto(
    int CandidateCount,
    int ObservationCount,
    int CompetitorCount,
    int OutlierCount,
    decimal? Average,
    decimal? WeightedAverage,
    decimal? Median,
    decimal? Minimum,
    decimal? Maximum,
    decimal? StandardDeviation,
    decimal? P25,
    decimal? P40,
    decimal? P50,
    decimal? P60,
    decimal? P65,
    decimal? P75,
    decimal? LowerMarket,
    decimal? UpperMarket,
    decimal TargetPercentile,
    decimal CompetitiveCeilingPercentile,
    decimal? TargetMarketPrice,
    decimal? CompetitiveCeiling,
    decimal ConfidenceScore,
    bool HasSufficientMarketData,
    bool CarrierFallbackUsed,
    string AlgorithmVersion,
    IReadOnlyCollection<MarketBenchmarkObservationDto> Observations
);

public sealed record AutoPricingPositionDto(
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
    string Status
);

public sealed record AutoPricingAdjustmentDto(
    Guid RateDetailId,
    string ChargeCode,
    string CurrencyCode,
    decimal Quantity,
    decimal OriginalUnitSaleAmount,
    decimal SuggestedUnitSaleAmount,
    decimal OriginalTotalSaleUsd,
    decimal SuggestedTotalSaleUsd,
    decimal AdjustmentAmountUsd,
    bool WasAdjusted,
    bool IsProtected,
    int? AdjustmentPriority,
    string? AdjustmentStrategy,
    string? ProtectionReason
);

public sealed record AutoPricingValidationIssueDto(
    string Code,
    string Message,
    bool IsBlocking
);

public sealed record AutoPricingCalculationDto(
    Guid DecisionId,
    Guid RateId,
    string Status,
    string ApplicationMode,
    bool CanApply,
    bool ShouldAutoApply,
    bool RequiresReview,
    decimal DesiredSalePrice,
    decimal AppliedAdjustmentAmount,
    decimal UnallocatedAdjustmentAmount,
    decimal MarketDeviationPercentage,
    string AlgorithmVersion,
    AutoPricingPositionDto Position,
    MarketBenchmarkDto Benchmark,
    IReadOnlyCollection<AutoPricingAdjustmentDto> Adjustments,
    IReadOnlyCollection<AutoPricingValidationIssueDto> ValidationIssues
);

public sealed record AutoPricingDecisionDto(
    Guid Id,
    Guid RateId,
    DateTime CalculatedAtUtc,
    Guid? ComparisonIncotermId,
    Guid ComparisonPolId,
    Guid? ComparisonPoeId,
    Guid? ComparisonPodId,
    Guid? ComparisonContainerTypeId,
    string ComparisonMode,
    Guid? ComparisonCarrierId,
    decimal CostTotal,
    decimal OriginalSaleTotal,
    decimal SuggestedSaleTotal,
    decimal FinalSaleTotal,
    decimal? Average,
    decimal? WeightedAverage,
    decimal? Median,
    decimal? P25,
    decimal? P40,
    decimal? P50,
    decimal? P60,
    decimal? P65,
    decimal? P75,
    decimal? TargetMarketPrice,
    decimal? CompetitiveCeiling,
    int CompetitorCount,
    int ObservationCount,
    decimal ConfidenceScore,
    string AlgorithmVersion,
    bool WasAutoApplied,
    bool WasManuallyModified,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<MarketBenchmarkObservationDto> Observations
);

public sealed record AutoPricingApplyDto(
    Guid DecisionId,
    Guid RateId,
    int AdjustedChargeCount,
    decimal PreviousSaleTotal,
    decimal AppliedSaleTotal,
    decimal AppliedMarginPercentage,
    string Status
);

public sealed record AutoPricingOverrideDto(
    Guid DecisionId,
    Guid RateId,
    decimal FinalSaleTotal,
    decimal MarginPercentage,
    string Reason
);

public sealed record AutoPricingApprovalDto(
    Guid DecisionId,
    Guid RateId,
    Guid ReviewedByUserId,
    DateTime ReviewedAtUtc,
    string Outcome
);
