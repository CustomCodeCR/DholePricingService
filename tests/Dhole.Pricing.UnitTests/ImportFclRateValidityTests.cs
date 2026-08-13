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

    private static ImportFclRates CreateRate(DateTime validFrom, DateTime validTo)
    {
        static CatalogSnapshot Snapshot(string prefix) =>
            new(Guid.NewGuid(), prefix, $"{prefix}-001", prefix.ToLowerInvariant());

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
}
