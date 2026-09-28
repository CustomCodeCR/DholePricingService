using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class MarketPricingPipelineIntegrationTests
{
    private static readonly DateTime ReferenceDate = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task BenchmarkPipeline_RecalculatesWhenMarketKeyDimensionsChange()
    {
        var ids = PipelineIds.Create();
        var baselineKey = ids.ToKey(ids.Fca, ids.Pol, ids.Poe, ids.Pod, ids.Container40Hc, ShipmentMode.Fcl, ids.Pil);
        var candidates = new[]
        {
            CreateObservation("Competitor A", baselineKey, ids.Pil, 7100m),
            CreateObservation("Competitor B", baselineKey, ids.Pil, 7250m),
            CreateObservation("Competitor C", baselineKey, ids.Pil, 7400m),
            CreateObservation("Competitor D", baselineKey, ids.Maersk, 7300m),
        };

        var comparator = new CompetitorRateComparabilityService(
            null!,
            new MarketComparabilityOptions
            {
                MinimumPrimaryCarrierObservations = 2,
                MinimumCompetitorsForSufficientMarket = 2,
                MinimumObservationsForSufficientMarket = 2,
            }
        );
        var adapter = new CandidateComparabilityService(comparator, candidates);
        var benchmarkService = new MarketBenchmarkService(adapter, new MarketBenchmarkOptions());

        var baseline = await benchmarkService.CalculateAsync(
            new MarketBenchmarkRequest(baselineKey, ReferenceDate)
        );

        Assert.IsTrue(baseline.HasSufficientMarketData);
        Assert.IsFalse(baseline.CarrierFallbackUsed);
        Assert.AreEqual(4, baseline.CandidateCount);
        Assert.IsTrue(baseline.ObservationCount >= 3);

        var changedHardDimensions = new[]
        {
            ids.ToKey(ids.Fob, ids.Pol, ids.Poe, ids.Pod, ids.Container40Hc, ShipmentMode.Fcl, ids.Pil),
            ids.ToKey(ids.Fca, Guid.NewGuid(), ids.Poe, ids.Pod, ids.Container40Hc, ShipmentMode.Fcl, ids.Pil),
            ids.ToKey(ids.Fca, ids.Pol, Guid.NewGuid(), ids.Pod, ids.Container40Hc, ShipmentMode.Fcl, ids.Pil),
            ids.ToKey(ids.Fca, ids.Pol, ids.Poe, Guid.NewGuid(), ids.Container40Hc, ShipmentMode.Fcl, ids.Pil),
            ids.ToKey(ids.Fca, ids.Pol, ids.Poe, ids.Pod, ids.Container20Dv, ShipmentMode.Fcl, ids.Pil),
            ids.ToKey(ids.Fca, ids.Pol, ids.Poe, ids.Pod, ids.Container40Hc, ShipmentMode.Lcl, ids.Pil),
        };

        foreach (var changedKey in changedHardDimensions)
        {
            var recalculated = await benchmarkService.CalculateAsync(
                new MarketBenchmarkRequest(changedKey, ReferenceDate)
            );

            Assert.AreEqual(0, recalculated.ObservationCount);
            Assert.IsFalse(recalculated.HasSufficientMarketData);
            Assert.AreEqual(0m, recalculated.ConfidenceScore);
        }

        var changedCarrier = ids.ToKey(
            ids.Fca,
            ids.Pol,
            ids.Poe,
            ids.Pod,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.Maersk
        );
        var carrierRecalculated = await benchmarkService.CalculateAsync(
            new MarketBenchmarkRequest(changedCarrier, ReferenceDate)
        );

        Assert.IsTrue(carrierRecalculated.CarrierFallbackUsed);
        Assert.IsTrue(carrierRecalculated.ConfidenceScore < baseline.ConfidenceScore);
        Assert.AreEqual(8, adapter.RequestedKeys.Count);
        Assert.AreEqual(changedCarrier, adapter.RequestedKeys[^1]);
    }

    [TestMethod]
    public async Task FullPipeline_ChangedIncotermTurnsAutoPricingIntoInsufficientMarketData()
    {
        var ids = PipelineIds.Create();
        var baselineKey = ids.ToKey(ids.Fca, ids.Pol, ids.Poe, ids.Pod, ids.Container40Hc, ShipmentMode.Fcl, ids.Pil);
        var candidates = new[]
        {
            CreateObservation("Competitor A", baselineKey, ids.Pil, 7100m),
            CreateObservation("Competitor B", baselineKey, ids.Pil, 7250m),
            CreateObservation("Competitor C", baselineKey, ids.Pil, 7400m),
        };

        var comparator = new CompetitorRateComparabilityService(
            null!,
            new MarketComparabilityOptions
            {
                MinimumPrimaryCarrierObservations = 2,
                MinimumCompetitorsForSufficientMarket = 2,
                MinimumObservationsForSufficientMarket = 2,
            }
        );
        var benchmarkService = new MarketBenchmarkService(
            new CandidateComparabilityService(comparator, candidates),
            new MarketBenchmarkOptions()
        );
        var autoPricing = new AutoPricingService(benchmarkService);

        var baseline = await autoPricing.CalculateAsync(
            CreateAutoPricingRequest(baselineKey)
        );
        Assert.IsTrue(baseline.Benchmark.HasSufficientMarketData);
        Assert.AreNotEqual(AutoPricingStatus.InsufficientMarketData, baseline.Status);

        var changedIncotermKey = ids.ToKey(
            ids.Fob,
            ids.Pol,
            ids.Poe,
            ids.Pod,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.Pil
        );
        var recalculated = await autoPricing.CalculateAsync(
            CreateAutoPricingRequest(changedIncotermKey)
        );

        Assert.IsFalse(recalculated.Benchmark.HasSufficientMarketData);
        Assert.AreEqual(AutoPricingStatus.InsufficientMarketData, recalculated.Status);
        Assert.AreEqual(AutoPricingApplicationMode.SuggestOnly, recalculated.ApplicationMode);
        Assert.IsFalse(recalculated.ShouldAutoApply);
    }

    private static AutoPricingRequest CreateAutoPricingRequest(MarketComparisonKey key)
    {
        var profile = AutoPricingProfile.Create(
            "Integration",
            "INTEGRATION",
            minimumConfidenceForAutoApply: 80m,
            minimumConfidenceForSuggestion: 60m,
            minimumCompetitorCount: 3,
            minimumObservationCount: 3
        );

        return new AutoPricingRequest(
            Guid.NewGuid(),
            key,
            ReferenceDate,
            MinimumMarginPercentage: 12m,
            profile,
            [
                ChargePricingRule.Create(
                    "OCEAN_FREIGHT",
                    true,
                    100,
                    0m,
                    null,
                    null,
                    ChargeAdjustmentStrategy.Priority
                ),
            ],
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
            ]
        );
    }

    private static CompetitorRateObservation CreateObservation(
        string competitor,
        MarketComparisonKey key,
        Guid carrierId,
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
            ReferenceDate.AddDays(-5),
            ReferenceDate.AddDays(20),
            MarketRateBasis.PerContainer,
            amount,
            0m,
            0m,
            0m,
            0m,
            amount,
            1m,
            "{}",
            ReferenceDate.AddDays(-1)
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
            carrierId,
            carrierId == key.CarrierId ? "PIL" : "MAERSK",
            carrierId.ToString("N"),
            key.ContainerTypeId,
            "40HC",
            1,
            amount,
            amount,
            "USD",
            amount,
            1m,
            ReferenceDate,
            1m
        );

        return observation;
    }

    private sealed class CandidateComparabilityService(
        CompetitorRateComparabilityService service,
        IReadOnlyCollection<CompetitorRateObservation> candidates
    ) : ICompetitorRateComparabilityService
    {
        public List<MarketComparisonKey> RequestedKeys { get; } = [];

        public Task<MarketComparabilityResult> CompareAsync(
            MarketComparisonKey key,
            DateTime referenceDate,
            CancellationToken cancellationToken = default
        )
        {
            RequestedKeys.Add(key);
            return Task.FromResult(service.CompareCandidates(key, referenceDate, candidates));
        }
    }

    private sealed record PipelineIds(
        Guid Fca,
        Guid Fob,
        Guid Pol,
        Guid Poe,
        Guid Pod,
        Guid Container40Hc,
        Guid Container20Dv,
        Guid Pil,
        Guid Maersk
    )
    {
        public static PipelineIds Create() => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        );

        public MarketComparisonKey ToKey(
            Guid incoterm,
            Guid pol,
            Guid poe,
            Guid pod,
            Guid equipment,
            ShipmentMode mode,
            Guid carrier
        ) => new(incoterm, pol, poe, pod, equipment, mode, carrier);
    }
}
