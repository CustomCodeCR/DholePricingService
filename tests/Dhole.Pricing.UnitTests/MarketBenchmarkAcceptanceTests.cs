using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class MarketBenchmarkAcceptanceTests
{
    [TestMethod]
    public async Task CalculateAsync_ReturnsRangeTargetCeilingConfidenceSample_AndKeepsObservationAudit()
    {
        var key = new MarketComparisonKey(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShipmentMode.Fcl,
            Guid.NewGuid()
        );
        var referenceDate = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

        var values = new[] { 7000m, 7200m, 7300m, 20000m };
        var comparable = values
            .Select((value, index) =>
            {
                var observation = CreateObservation(
                    $"Competitor {index + 1}",
                    key,
                    referenceDate,
                    value
                );

                return new ComparableMarketObservation(
                    observation,
                    new MarketComparabilityDimensionScores(
                        1m, 1m, 1m, 1m, 1m, 0.95m, 1m, 1m, 1m
                    ),
                    StructuralComparabilityWeight: 1m,
                    ComparabilityScore: 99m,
                    RecencyWeight: 0.95m,
                    FinalWeight: 0.95m - (index * 0.05m),
                    CarrierMarket: MarketCarrierMarketKind.Primary,
                    IsExactMatch: true,
                    WasIncluded: true,
                    ExclusionReason: null
                );
            })
            .ToArray();

        var comparison = new MarketComparabilityResult(
            key,
            referenceDate,
            CandidateCount: comparable.Length,
            IncludedObservationCount: comparable.Length,
            CompetitorCount: comparable.Length,
            ExactMatchCount: comparable.Length,
            CarrierExactMatchCount: comparable.Length,
            CarrierFallbackUsed: false,
            HasSufficientMarketData: true,
            ConfidenceScore: 90m,
            ConfidenceLevel: MarketConfidenceLevel.High,
            ConfidenceBreakdown: new MarketConfidenceBreakdown(
                1m, 1m, 1m, 1m, 1m, 1m, 1m, 0.95m, 1m, 90m
            ),
            AlgorithmVersion: "comparability-test-v1",
            Observations: comparable
        );

        var service = new MarketBenchmarkService(
            new StaticComparabilityService(comparison),
            new MarketBenchmarkOptions()
        );

        var result = await service.CalculateAsync(
            new MarketBenchmarkRequest(
                key,
                referenceDate,
                MarketBenchmarkAmountKind.AllIn,
                TargetPercentile: 60m,
                CompetitiveCeilingPercentile: 65m
            )
        );

        Assert.AreEqual(4, result.CandidateCount);
        Assert.AreEqual(1, result.OutlierCount);
        Assert.AreEqual(3, result.ObservationCount);
        Assert.AreEqual(3, result.CompetitorCount);

        Assert.IsNotNull(result.LowerMarket);
        Assert.IsNotNull(result.UpperMarket);
        Assert.IsNotNull(result.TargetMarketPrice);
        Assert.IsNotNull(result.CompetitiveCeiling);
        Assert.IsTrue(result.CompetitiveCeiling >= result.TargetMarketPrice);
        Assert.IsTrue(result.ConfidenceScore > 0m);
        Assert.IsTrue(result.HasSufficientMarketData);

        Assert.AreEqual(4, result.Observations.Count);
        Assert.AreEqual(1, result.Observations.Count(x => x.IsOutlier));
        Assert.AreEqual(3, result.Observations.Count(x => x.WasIncluded));
        Assert.IsTrue(result.Observations.Where(x => x.WasIncluded).All(x => x.FinalWeight > 0m));
    }

    private static CompetitorRateObservation CreateObservation(
        string competitor,
        MarketComparisonKey key,
        DateTime referenceDate,
        decimal amount
    )
    {
        var observation = CompetitorRateObservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            competitor,
            Guid.NewGuid(),
            Guid.NewGuid(),
            key.Mode,
            "USD",
            referenceDate.AddDays(-7),
            referenceDate.AddDays(14),
            MarketRateBasis.PerContainer,
            amount,
            0m,
            0m,
            0m,
            0m,
            amount,
            1m,
            "{}",
            referenceDate.AddDays(-1)
        );

        observation.ApplyNormalization(
            key.IncotermId,
            "FCA",
            key.PolId,
            "Shanghai",
            "CNSGH",
            key.PoeId,
            "Puerto Caldera",
            "CRCAL",
            key.PodId,
            "San José",
            "CRSJO",
            key.CarrierId,
            "PIL",
            "PIL",
            key.ContainerTypeId,
            "40HC",
            1,
            amount,
            amount,
            "USD",
            amount,
            1m,
            referenceDate,
            1m
        );

        return observation;
    }

    private sealed class StaticComparabilityService(MarketComparabilityResult result)
        : ICompetitorRateComparabilityService
    {
        public Task<MarketComparabilityResult> CompareAsync(
            MarketComparisonKey key,
            DateTime referenceDate,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }
}
