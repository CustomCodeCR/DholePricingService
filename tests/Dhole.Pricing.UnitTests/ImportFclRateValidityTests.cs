using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportFclRateValidityTests
{
    [TestMethod]
    public void IsEffectiveOn_WhenDateIsInsideValidityWindow_ReturnsTrue()
    {
        var rate = CreateRate(new DateTime(2026, 8, 8), new DateTime(2026, 8, 14));

        Assert.IsTrue(rate.IsEffectiveOn(new DateTime(2026, 8, 11)));
        Assert.IsTrue(rate.IsEffectiveOn(new DateTime(2026, 8, 8)));
        Assert.IsTrue(rate.IsEffectiveOn(new DateTime(2026, 8, 14)));
    }

    [TestMethod]
    public void IsEffectiveOn_WhenDateIsOutsideValidityWindow_ReturnsFalse()
    {
        var rate = CreateRate(new DateTime(2026, 8, 8), new DateTime(2026, 8, 14));

        Assert.IsFalse(rate.IsEffectiveOn(new DateTime(2026, 8, 7)));
        Assert.IsFalse(rate.IsEffectiveOn(new DateTime(2026, 8, 15)));
    }

    [TestMethod]
    public void Approve_DoesNotMarkImportedRateAsUsed()
    {
        var rate = CreateRate(new DateTime(2026, 8, 8), new DateTime(2026, 8, 14));

        rate.Approve();

        Assert.AreEqual(0, rate.UsedAsRateCount);
        Assert.IsNull(rate.CreatedAsRateHeaderId);
    }

    [TestMethod]
    public void CreatedAsRate_MarksImportedRateAsUsed()
    {
        var rate = CreateRate(new DateTime(2026, 8, 8), new DateTime(2026, 8, 14));
        var rateHeaderId = Guid.NewGuid();

        rate.CreatedAsRate(rateHeaderId);

        Assert.AreEqual(1, rate.UsedAsRateCount);
        Assert.AreEqual(rateHeaderId, rate.CreatedAsRateHeaderId);
    }

    [TestMethod]
    public void ApprovedUnusedRate_CanBeReviewedAndRejected()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.Approve();

        Assert.IsFalse(rate.HasBeenUsedAsRate);
        Assert.IsTrue(rate.CanBeManuallyReviewed);
        Assert.IsTrue(rate.CanBeRejected);
    }

    [TestMethod]
    public void ApprovedUsedRate_CannotBeReviewedOrRejected()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.CreatedAsRate(Guid.NewGuid());

        Assert.IsTrue(rate.HasBeenUsedAsRate);
        Assert.IsFalse(rate.CanBeManuallyReviewed);
        Assert.IsFalse(rate.CanBeRejected);
    }

    [TestMethod]
    public void ApprovedUnusedRate_CanBeInactivatedAndIsNoLongerEffective()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.Approve();

        rate.Inactivate();

        Assert.AreEqual(ImportStatus.Inactive, rate.Status);
        Assert.IsFalse(rate.IsEffectiveOn(new DateTime(2026, 9, 18)));
    }

    [TestMethod]
    public void ApprovedUsedRate_CanBeInactivatedWithoutLosingUsageHistory()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        var rateHeaderId = Guid.NewGuid();
        rate.CreatedAsRate(rateHeaderId);

        rate.Inactivate();

        Assert.AreEqual(ImportStatus.Inactive, rate.Status);
        Assert.AreEqual(1, rate.UsedAsRateCount);
        Assert.AreEqual(rateHeaderId, rate.CreatedAsRateHeaderId);
        Assert.IsTrue(rate.HasBeenUsedAsRate);
    }

    [TestMethod]
    public void CreatedAsRate_WhenImportRateIsInactive_Throws()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.Approve();
        rate.Inactivate();

        AssertThrowsInvalidOperation(() => rate.CreatedAsRate(Guid.NewGuid()));
    }

    [TestMethod]
    public void ApplyManualReview_WhenApprovedAndUnused_UpdatesRateAndPreservesApproval()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.Approve();

        ApplyReview(rate, 7300m, 65m);

        Assert.AreEqual(ImportStatus.Approved, rate.Status);
        Assert.AreEqual((decimal?)7300m, rate.OceanFreight);
        Assert.AreEqual((decimal?)65m, rate.Surcharges);
        Assert.AreEqual((decimal?)7365m, rate.TotalCost);
    }

    [TestMethod]
    public void ApplyManualReview_WhenApprovedAndUsed_Throws()
    {
        var rate = CreateRate(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21));
        rate.CreatedAsRate(Guid.NewGuid());

        AssertThrowsInvalidOperation(() => ApplyReview(rate, 7300m, 65m));
    }

    [TestMethod]
    public void HistoricalQuote_CanUseExpiredImportedFreight_OutsideSupplierWindow()
    {
        // La QUO se registra el 26/10 con embarque 10/10 y vencimiento 20/10;
        // la vigencia del proveedor fue en septiembre y no debe bloquearla.
        var source = CreateRate(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        source.Approve();
        source.ExpireIfNeeded(new DateTime(2026, 10, 26));

        Assert.AreEqual(ImportStatus.Expired, source.Status);
        Assert.IsTrue(source.CanBeUsedAsQuoteFreight(canApprovePending: false));

        var rateHeaderId = Guid.NewGuid();
        source.CreatedAsRate(rateHeaderId);

        Assert.AreEqual(ImportStatus.Expired, source.Status);
        Assert.AreEqual(rateHeaderId, source.CreatedAsRateHeaderId);
        Assert.AreEqual(1, source.UsedAsRateCount);
    }

    [TestMethod]
    public void HistoricalQuote_PreAuthorizedRateCanBeUsedWithoutManualApproval()
    {
        var source = CreateRate(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        Assert.AreEqual(ImportStatus.PreAuthorized, source.Status);
        Assert.IsTrue(source.CanBeUsedAsQuoteFreight(canApprovePending: false));
    }

    [TestMethod]
    public void HistoricalQuote_RejectedAndInactiveFreightsAreNotEligible()
    {
        var rejected = CreateRate(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        rejected.Reject(updatedBy: null);
        Assert.IsFalse(rejected.CanBeUsedAsQuoteFreight(canApprovePending: true));

        var inactive = CreateRate(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        inactive.Inactivate();
        Assert.IsFalse(inactive.CanBeUsedAsQuoteFreight(canApprovePending: true));
    }

    private static void AssertThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            Assert.Fail("Se esperaba InvalidOperationException.");
        }
        catch (InvalidOperationException)
        {
            // Expected.
        }
    }

    private static void ApplyReview(ImportFclRates rate, decimal oceanFreight, decimal surcharges)
    {
        rate.ApplyManualReview(
            Snapshot("Profile"),
            Snapshot("POL"),
            Snapshot("POE"),
            Snapshot("POD"),
            Snapshot("Carrier"),
            Snapshot("Agent"),
            Snapshot("40HC"),
            Snapshot("USD"),
            commodity: "Solar Panels/Solar Modules/LED Lights",
            spaceComment: "Solar Panels/Solar Modules/LED Lights",
            oceanFreight: oceanFreight,
            originCharges: 0m,
            destinationCharges: 0m,
            surcharges: surcharges,
            totalSale: null,
            freeDays: 21,
            transitDays: 0,
            validFrom: new DateTime(2026, 9, 15),
            validTo: new DateTime(2026, 9, 21),
            updatedBy: null
        );
    }

    private static ImportFclRates CreateRate(DateTime validFrom, DateTime validTo)
    {
        return ImportFclRates.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ImportSourceType.Email,
            Snapshot("Profile"),
            Snapshot("POL"),
            Snapshot("POE"),
            Snapshot("POD"),
            Snapshot("Carrier"),
            Snapshot("Agent"),
            Snapshot("40HC"),
            Snapshot("USD"),
            commodity: null,
            spaceComment: null,
            oceanFreight: 6300m,
            originCharges: null,
            destinationCharges: null,
            surcharges: null,
            totalCost: 6300m,
            totalSale: 6300m,
            profit: 0m,
            margin: 0m,
            freeDays: 21,
            transitDays: 42,
            validFrom,
            validTo,
            rawDataJson: "{}",
            createdBy: null
        );
    }

    private static CatalogSnapshot Snapshot(string prefix) =>
        new(Guid.NewGuid(), prefix, $"{prefix}-001", prefix.ToLowerInvariant());
}
