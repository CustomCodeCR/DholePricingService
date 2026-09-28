using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class AutoPricingAcceptanceTests
{
    [TestMethod]
    public async Task CalculateAsync_HighConfidenceAndSufficientSample_CanAutoApply()
    {
        var benchmark = CreateBenchmark(
            target: 7300m,
            ceiling: 7500m,
            lower: 7000m,
            confidence: 92m,
            competitorCount: 3,
            observationCount: 6,
            sufficient: true
        );

        var service = new AutoPricingService(new StaticBenchmarkService(benchmark));
        var request = CreateRequest(
            minimumMargin: 12m,
            charges:
            [
                new AutoPricingChargeInput(
                    Guid.NewGuid(),
                    "OCEAN_FREIGHT",
                    CostAmount: 6000m,
                    SaleAmount: 7000m,
                    Quantity: 1m,
                    CurrencyCode: "USD",
                    CurrencyToUsdRate: 1m,
                    IsFixedAmount: false
                ),
            ],
            rules:
            [
                ChargePricingRule.Create(
                    "OCEAN_FREIGHT",
                    canAutoAdjust: true,
                    adjustmentPriority: 100,
                    minimumMarkup: 0m,
                    maximumMarkup: null,
                    maximumAdjustmentAmount: null,
                    adjustmentStrategy: ChargeAdjustmentStrategy.Priority
                ),
            ]
        );

        var result = await service.CalculateAsync(request);

        Assert.IsTrue(result.CanApply);
        Assert.IsTrue(result.ShouldAutoApply);
        Assert.IsFalse(result.RequiresReview);
        Assert.AreEqual(AutoPricingApplicationMode.AutoApply, result.ApplicationMode);
        Assert.AreEqual(AutoPricingStatus.Calculated, result.Status);
        Assert.AreEqual(7300m, result.Position.SuggestedSalePrice);
        Assert.AreEqual(MarketPositionStatus.Competitive, result.Position.Status);
    }

    [TestMethod]
    public async Task CalculateAsync_MinimumMarginWinsEvenWhenItPushesPriceAboveMarket()
    {
        var benchmark = CreateBenchmark(
            target: 7000m,
            ceiling: 7100m,
            lower: 6800m,
            confidence: 90m,
            competitorCount: 3,
            observationCount: 5,
            sufficient: true
        );

        var service = new AutoPricingService(new StaticBenchmarkService(benchmark));
        var request = CreateRequest(
            minimumMargin: 12m,
            charges:
            [
                new AutoPricingChargeInput(
                    Guid.NewGuid(),
                    "OCEAN_FREIGHT",
                    CostAmount: 6300m,
                    SaleAmount: 7000m,
                    Quantity: 1m,
                    CurrencyCode: "USD",
                    CurrencyToUsdRate: 1m,
                    IsFixedAmount: false
                ),
            ],
            rules:
            [
                ChargePricingRule.Create(
                    "OCEAN_FREIGHT",
                    canAutoAdjust: true,
                    adjustmentPriority: 100,
                    minimumMarkup: 0m,
                    maximumMarkup: null,
                    maximumAdjustmentAmount: null,
                    adjustmentStrategy: ChargeAdjustmentStrategy.Priority
                ),
            ]
        );

        var result = await service.CalculateAsync(request);

        Assert.IsTrue(result.Position.SuggestedSalePrice >= result.Position.MinimumSalePrice);
        Assert.IsTrue(result.Position.SuggestedMargin >= 12m);
        Assert.IsTrue(result.Position.SuggestedSalePrice > benchmark.CompetitiveCeiling);
        Assert.AreEqual(MarketPositionStatus.AboveCompetitiveRange, result.Position.Status);
        Assert.IsTrue(result.RequiresReview);
        Assert.IsTrue(result.ValidationIssues.Any(x => x.Code == "above_competitive_range"));
    }

    [TestMethod]
    public async Task CalculateAsync_DistributesOnlyAcrossAdjustableCharges()
    {
        var benchmark = CreateBenchmark(
            target: 7350m,
            ceiling: 7600m,
            lower: 7000m,
            confidence: 90m,
            competitorCount: 3,
            observationCount: 5,
            sufficient: true
        );

        var adjustableId = Guid.NewGuid();
        var fixedId = Guid.NewGuid();

        var service = new AutoPricingService(new StaticBenchmarkService(benchmark));
        var request = CreateRequest(
            minimumMargin: 0m,
            charges:
            [
                new AutoPricingChargeInput(
                    adjustableId,
                    "OCEAN_FREIGHT",
                    CostAmount: 6000m,
                    SaleAmount: 7000m,
                    Quantity: 1m,
                    CurrencyCode: "USD",
                    CurrencyToUsdRate: 1m,
                    IsFixedAmount: false
                ),
                new AutoPricingChargeInput(
                    fixedId,
                    "HBL",
                    CostAmount: 0m,
                    SaleAmount: 50m,
                    Quantity: 1m,
                    CurrencyCode: "USD",
                    CurrencyToUsdRate: 1m,
                    IsFixedAmount: true
                ),
            ],
            rules:
            [
                ChargePricingRule.Create(
                    "OCEAN_FREIGHT",
                    canAutoAdjust: true,
                    adjustmentPriority: 100,
                    minimumMarkup: 0m,
                    maximumMarkup: null,
                    maximumAdjustmentAmount: null,
                    adjustmentStrategy: ChargeAdjustmentStrategy.Priority
                ),
                ChargePricingRule.Create(
                    "HBL",
                    canAutoAdjust: true,
                    adjustmentPriority: 50,
                    minimumMarkup: 0m,
                    maximumMarkup: null,
                    maximumAdjustmentAmount: null,
                    adjustmentStrategy: ChargeAdjustmentStrategy.Priority
                ),
            ]
        );

        var result = await service.CalculateAsync(request);
        var adjustable = result.Adjustments.Single(x => x.RateDetailId == adjustableId);
        var fixedCharge = result.Adjustments.Single(x => x.RateDetailId == fixedId);

        Assert.IsTrue(adjustable.WasAdjusted);
        Assert.IsFalse(adjustable.IsProtected);
        Assert.AreEqual(300m, adjustable.AdjustmentAmountUsd);

        Assert.IsFalse(fixedCharge.WasAdjusted);
        Assert.IsTrue(fixedCharge.IsProtected);
        Assert.AreEqual("fixed_amount", fixedCharge.ProtectionReason);
        Assert.AreEqual(50m, fixedCharge.SuggestedTotalSaleUsd);
        Assert.AreEqual(0m, fixedCharge.AdjustmentAmountUsd);
    }

    private static AutoPricingRequest CreateRequest(
        decimal minimumMargin,
        IReadOnlyCollection<AutoPricingChargeInput> charges,
        IReadOnlyCollection<ChargePricingRule> rules
    )
    {
        var profile = AutoPricingProfile.Create(
            "Default",
            "DEFAULT",
            targetPercentile: 60m,
            competitiveCeilingPercentile: 65m,
            minimumConfidenceForAutoApply: 80m,
            minimumConfidenceForSuggestion: 60m,
            minimumCompetitorCount: 3,
            minimumObservationCount: 3,
            maximumMarketDeviation: 25m
        );

        return new AutoPricingRequest(
            Guid.NewGuid(),
            new MarketComparisonKey(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ShipmentMode.Fcl,
                Guid.NewGuid()
            ),
            new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            minimumMargin,
            profile,
            rules,
            charges,
            MarketBenchmarkAmountKind.AllIn
        );
    }

    private static MarketBenchmarkResult CreateBenchmark(
        decimal target,
        decimal ceiling,
        decimal lower,
        decimal confidence,
        int competitorCount,
        int observationCount,
        bool sufficient
    ) =>
        new(
            ObservationCount: observationCount,
            CompetitorCount: competitorCount,
            CandidateCount: observationCount,
            OutlierCount: 0,
            Average: target,
            WeightedAverage: target,
            Median: target,
            Minimum: lower,
            Maximum: ceiling,
            StandardDeviation: 25m,
            Percentile25: lower,
            Percentile40: target - 50m,
            Percentile50: target,
            Percentile60: target,
            Percentile65: ceiling,
            Percentile75: ceiling,
            LowerMarket: lower,
            UpperMarket: ceiling,
            TargetPercentile: 60m,
            CompetitiveCeilingPercentile: 65m,
            TargetMarketPrice: target,
            CompetitiveCeiling: ceiling,
            ConfidenceScore: confidence,
            HasSufficientMarketData: sufficient,
            CarrierFallbackUsed: false,
            AlgorithmVersion: "benchmark-test-v1",
            Observations: Array.Empty<MarketObservationResult>()
        );

    private sealed class StaticBenchmarkService(MarketBenchmarkResult result)
        : IMarketBenchmarkService
    {
        public Task<MarketBenchmarkResult> CalculateAsync(
            MarketBenchmarkRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }
}
