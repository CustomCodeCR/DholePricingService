using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.AutoPricing;

public enum AutoPricingApplicationMode
{
    SuggestOnly = 0,
    AutoApplyWithReview = 1,
    AutoApply = 2,
}

public sealed record AutoPricingChargeInput(
    Guid RateDetailId,
    string ChargeCode,
    decimal CostAmount,
    decimal SaleAmount,
    decimal Quantity,
    string CurrencyCode,
    decimal CurrencyToUsdRate,
    bool IsFixedAmount
);

public sealed record AutoPricingRequest(
    Guid RateId,
    MarketComparisonKey MarketKey,
    DateTime ReferenceDate,
    decimal MinimumMarginPercentage,
    AutoPricingProfile Profile,
    IReadOnlyCollection<ChargePricingRule> ChargeRules,
    IReadOnlyCollection<AutoPricingChargeInput> Charges,
    MarketBenchmarkAmountKind BenchmarkAmountKind = MarketBenchmarkAmountKind.AllIn
);

public sealed record AutoPricingChargeAdjustment(
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
    ChargeAdjustmentStrategy? AdjustmentStrategy,
    string? ProtectionReason
);

public sealed record AutoPricingValidationIssue(
    string Code,
    string Message,
    bool IsBlocking
);

public sealed record AutoPricingProposal(
    Guid RateId,
    PricingMarketPosition Position,
    MarketBenchmarkResult Benchmark,
    AutoPricingStatus Status,
    AutoPricingApplicationMode ApplicationMode,
    bool CanApply,
    bool ShouldAutoApply,
    bool RequiresReview,
    decimal DesiredSalePrice,
    decimal AppliedAdjustmentAmount,
    decimal UnallocatedAdjustmentAmount,
    decimal MarketDeviationPercentage,
    string AlgorithmVersion,
    IReadOnlyCollection<AutoPricingChargeAdjustment> Adjustments,
    IReadOnlyCollection<AutoPricingValidationIssue> ValidationIssues
);

public sealed record AutoPricingApplyResult(
    Guid RateId,
    AutoPricingStatus Status,
    int AdjustedChargeCount,
    decimal PreviousSaleTotal,
    decimal AppliedSaleTotal,
    decimal AppliedMarginPercentage
);
