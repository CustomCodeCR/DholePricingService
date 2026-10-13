using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class MarketComparabilityAcceptanceTests
{
    private static readonly DateTime ReferenceDate = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void CompareCandidates_ExactFcaRouteEquipmentModeCarrier_IsPrioritized_AndHardMismatchesAreExcluded()
    {
        var ids = TestIds.Create();
        var service = CreateService();

        var exact = CreateObservation(
            "Competitor Exact",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            7250m
        );

        var fob = CreateObservation(
            "Competitor FOB",
            ids.Fob,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            7200m
        );

        var wrongEquipment = CreateObservation(
            "Competitor 20DV",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container20Dv,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            7000m
        );

        var lcl = CreateObservation(
            "Competitor LCL",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Lcl,
            ids.CarrierPil,
            6900m
        );

        var key = new MarketComparisonKey(
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil
        );

        var result = service.CompareCandidates(key, ReferenceDate, [exact, fob, wrongEquipment, lcl]);

        Assert.AreEqual(1, result.IncludedObservationCount);
        Assert.AreEqual(1, result.ExactMatchCount);

        var exactResult = result.Observations.Single(x => x.Observation.Id == exact.Id);
        Assert.IsTrue(exactResult.WasIncluded);
        Assert.IsTrue(exactResult.IsExactMatch);
        Assert.IsTrue(
            exactResult.ComparabilityScore >= 95m,
            $"El exact match debe conservar prioridad alta. Score: {exactResult.ComparabilityScore}."
        );

        Assert.AreEqual(
            "incoterm_mismatch",
            result.Observations.Single(x => x.Observation.Id == fob.Id).ExclusionReason
        );
        Assert.AreEqual(
            "equipment_mismatch",
            result.Observations.Single(x => x.Observation.Id == wrongEquipment.Id).ExclusionReason
        );
        Assert.AreEqual(
            "mode_mismatch",
            result.Observations.Single(x => x.Observation.Id == lcl.Id).ExclusionReason
        );
    }

    [TestMethod]
    public void CompareCandidates_ProvisionalObservation_CannotInfluenceAverage()
    {
        var ids = TestIds.Create();
        var pendingReview = CreateObservation(
            "Competitor with missing source validity",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            1m,
            extractionConfidence: 0m
        );

        var key = new MarketComparisonKey(
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil
        );
        var result = CreateService().CompareCandidates(
            key, ReferenceDate, [pendingReview]
        );

        Assert.AreEqual(0, result.IncludedObservationCount);
        var evaluated = result.Observations.Single();
        Assert.IsFalse(evaluated.WasIncluded);
        Assert.AreEqual("manual_review_required", evaluated.ExclusionReason);
    }

    [TestMethod]
    public void CompareCandidates_PrimaryCarrier_ReceivesMoreWeightThanSecondaryCarrier()
    {
        var ids = TestIds.Create();
        var service = CreateService();

        var pil = CreateObservation(
            "Competitor PIL",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            7250m
        );

        var maersk = CreateObservation(
            "Competitor MAERSK",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierMaersk,
            7250m
        );

        var key = new MarketComparisonKey(
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil
        );

        var result = service.CompareCandidates(key, ReferenceDate, [pil, maersk]);

        var pilResult = result.Observations.Single(x => x.Observation.Id == pil.Id);
        var maerskResult = result.Observations.Single(x => x.Observation.Id == maersk.Id);

        Assert.IsTrue(pilResult.WasIncluded);
        Assert.IsTrue(maerskResult.WasIncluded);
        Assert.IsTrue(pilResult.IsPrimaryCarrierMarket);
        Assert.IsFalse(maerskResult.IsPrimaryCarrierMarket);
        Assert.IsTrue(pilResult.ComparabilityScore > maerskResult.ComparabilityScore);
        Assert.IsTrue(pilResult.FinalWeight > maerskResult.FinalWeight);
    }

    [TestMethod]
    public void CompareCandidates_PoeOnlyTariff_MatchesRequestedPolWithoutPOD()
    {
        var ids = TestIds.Create();
        var quote = CreateObservation(
            "Competitor six origins with POE Caldera",
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            null,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil,
            8818.3333m
        );
        var key = new MarketComparisonKey(
            ids.Fca,
            ids.PolShanghai,
            ids.PoeCaldera,
            ids.PodSanJose,
            ids.Container40Hc,
            ShipmentMode.Fcl,
            ids.CarrierPil
        );
        var result = CreateService().CompareCandidates(key, ReferenceDate, [quote]);

        Assert.AreEqual(1, result.IncludedObservationCount);
        Assert.IsTrue(result.Observations.Single().WasIncluded);
        Assert.IsNull(quote.PodId);
        Assert.AreEqual(8818.3333m, quote.NormalizedAllIn);
    }

    [TestMethod]
    public void CompareCandidates_ExpiredSeptemberRates_DoNotAffectOctoberBenchmark()
    {
        var ids = TestIds.Create();
        var referenceDate = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var expired = CreateObservation(
            "Expired September price", ids.Fca, ids.PolShanghai, ids.PoeCaldera,
            ids.PodSanJose, ids.Container40Hc, ShipmentMode.Fcl, ids.CarrierPil, 10705m
        );
        expired.ApplyManualReview(
            "USD", 10705m,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)
        );
        var current = CreateObservation(
            "October price", ids.Fca, ids.PolShanghai, ids.PoeCaldera,
            ids.PodSanJose, ids.Container40Hc, ShipmentMode.Fcl, ids.CarrierPil, 8818.33m
        );
        current.ApplyManualReview(
            "USD", 8818.33m,
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)
        );
        var key = new MarketComparisonKey(
            ids.Fca, ids.PolShanghai, ids.PoeCaldera, ids.PodSanJose,
            ids.Container40Hc, ShipmentMode.Fcl, ids.CarrierPil
        );
        var comparison = CreateService().CompareCandidates(key, referenceDate, [expired, current]);

        Assert.AreEqual(1, comparison.IncludedObservationCount);
        Assert.AreEqual("expired",
            comparison.Observations.Single(x => x.Observation.Id == expired.Id).ExclusionReason);
        Assert.IsTrue(comparison.Observations.Single(x => x.Observation.Id == current.Id).WasIncluded);
    }

    [TestMethod]
    public void CompareCandidates_StartsMoreThanSevenDaysAway_RemainsExcluded()
    {
        var ids = TestIds.Create();
        var referenceDate = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var distant = CreateObservation(
            "Too early", ids.Fca, ids.PolShanghai, ids.PoeCaldera,
            ids.PodSanJose, ids.Container40Hc, ShipmentMode.Fcl, ids.CarrierPil, 7100m
        );
        distant.ApplyManualReview(
            "USD", 7100m,
            referenceDate.AddDays(8), referenceDate.AddDays(14)
        );
        var key = new MarketComparisonKey(
            ids.Fca, ids.PolShanghai, ids.PoeCaldera, ids.PodSanJose,
            ids.Container40Hc, ShipmentMode.Fcl, ids.CarrierPil
        );

        var comparison = CreateService().CompareCandidates(key, referenceDate, [distant]);
        Assert.AreEqual(0, comparison.IncludedObservationCount);
        Assert.AreEqual("not_yet_valid", comparison.Observations.Single().ExclusionReason);
    }

    private static CompetitorRateComparabilityService CreateService() =>
        new(
            null!,
            new MarketComparabilityOptions
            {
                MinimumCompetitorsForSufficientMarket = 2,
                MinimumObservationsForSufficientMarket = 2,
            }
        );

    private static CompetitorRateObservation CreateObservation(
        string competitor,
        Guid incotermId,
        Guid polId,
        Guid poeId,
        Guid? podId,
        Guid containerTypeId,
        ShipmentMode mode,
        Guid carrierId,
        decimal allIn,
        decimal extractionConfidence = 1m
    )
    {
        var observation = CompetitorRateObservation.Create(
            competitorTariffId: Guid.NewGuid(),
            competitorCompanyId: Guid.NewGuid(),
            competitorCompanyName: competitor,
            sourceDocumentId: Guid.NewGuid(),
            sourceImportId: Guid.NewGuid(),
            mode: mode,
            currency: "USD",
            validFrom: ReferenceDate.AddDays(-10),
            validTo: ReferenceDate.AddDays(10),
            rateBasis: MarketRateBasis.PerContainer,
            oceanFreight: allIn,
            originCharges: 0m,
            destinationCharges: 0m,
            inlandCharges: 0m,
            otherCharges: 0m,
            originalAmount: allIn,
            extractionConfidence: extractionConfidence,
            rawPayloadJson: "{}",
            importedAtUtc: ReferenceDate.AddDays(-1)
        );

        observation.ApplyNormalization(
            incotermId,
            incotermId.ToString("D"),
            polId,
            "Shanghai",
            "CNSGH",
            poeId,
            "Puerto Caldera",
            "CRCAL",
            podId,
            podId.HasValue ? "San José" : null,
            podId.HasValue ? "CRSJO" : null,
            carrierId,
            carrierId.ToString("D"),
            carrierId.ToString("N"),
            containerTypeId,
            containerTypeId.ToString("N"),
            quantity: 1,
            normalizedOceanFreight: allIn,
            normalizedAllIn: allIn,
            normalizedCurrency: "USD",
            normalizedAmount: allIn,
            exchangeRate: 1m,
            exchangeRateDate: ReferenceDate,
            normalizationConfidence: 1m
        );

        return observation;
    }

    private sealed record TestIds(
        Guid Fca,
        Guid Fob,
        Guid PolShanghai,
        Guid PoeCaldera,
        Guid PodSanJose,
        Guid Container40Hc,
        Guid Container20Dv,
        Guid CarrierPil,
        Guid CarrierMaersk
    )
    {
        public static TestIds Create() => new(
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
    }
}
