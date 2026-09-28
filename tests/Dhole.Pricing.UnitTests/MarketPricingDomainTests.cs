using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class MarketPricingDomainTests
{
    [TestMethod]
    public void PricingMarketDecision_ManualOverride_PreservesOriginalCalculation()
    {
        var rateId = Guid.NewGuid();
        var key = new MarketComparisonKey(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShipmentMode.Fcl,
            Guid.NewGuid()
        );

        var decision = PricingMarketDecision.Create(
            rateId,
            key,
            costTotal: 6300m,
            originalSaleTotal: 7200m,
            suggestedSaleTotal: 7320m,
            finalSaleTotal: 7320m,
            average: 7280m,
            weightedAverage: 7280m,
            median: 7250m,
            p25: 7050m,
            p40: 7180m,
            p50: 7250m,
            p60: 7320m,
            p65: 7355m,
            p75: 7420m,
            targetMarketPrice: 7320m,
            competitiveCeiling: 7355m,
            competitorCount: 3,
            observationCount: 8,
            confidenceScore: 84m,
            algorithmVersion: "test-v1",
            wasAutoApplied: false
        );

        decision.MarkManualOverride(7295m);

        Assert.AreEqual(7200m, decision.OriginalSaleTotal);
        Assert.AreEqual(7320m, decision.SuggestedSaleTotal);
        Assert.AreEqual(7295m, decision.FinalSaleTotal);
        Assert.IsTrue(decision.WasManuallyModified);
        Assert.IsFalse(decision.WasAutoApplied);
    }

    [TestMethod]
    public void PricingMarketDecisionObservation_PreservesComparabilityAndWeightForAudit()
    {
        var observation = PricingMarketDecisionObservation.Create(
            pricingMarketDecisionId: Guid.NewGuid(),
            competitorRateObservationId: Guid.NewGuid(),
            competitorCompanyId: Guid.NewGuid(),
            originalAmount: 7250m,
            normalizedAmount: 7250m,
            comparabilityScore: 97.5m,
            recencyWeight: 0.91m,
            finalWeight: 0.88m,
            isOutlier: false,
            wasIncluded: true,
            exclusionReason: null
        );

        Assert.AreEqual(97.5m, observation.ComparabilityScore);
        Assert.AreEqual(0.91m, observation.RecencyWeight);
        Assert.AreEqual(0.88m, observation.FinalWeight);
        Assert.IsTrue(observation.WasIncluded);
        Assert.IsFalse(observation.IsOutlier);
    }
}
