using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateMixedCurrencyTests
{
    [TestMethod]
    public void SetAmounts_WithUsdAndCrc_ProducesEquivalentTotalsInBothCurrencies()
    {
        var rate = RateHeader.Create(
            "QUO-TEST", null, null, null, null, null, null, null,
            Guid.NewGuid(), "Shanghai", "CNSHA", Guid.NewGuid(), "Caldera, Costa Rica", "CRCAL",
            null, null, null, Guid.NewGuid(), "40 HC", "40HC", null, null, null,
            Guid.NewGuid(), "USD", "USD", 0, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1, null, null, null, null, null, null, null, null, RateType.Spot, null);
        rate.ConfigureExchangeRateSnapshot(500m, 510m, 510m, DateTime.UtcNow.Date, DateTime.UtcNow, "Test", false, null);
        rate.AddRateDetail(rate.Id, null, "Freight", CostDetailType.Freight, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "USD", "USD", 100m, 150m, null, 1m, null);
        rate.AddRateDetail(rate.Id, null, "Aduanas", CostDetailType.CustomsCharge, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 51000m, 76500m, null, 1m, null);

        rate.SetAmounts();

        Assert.AreEqual(200m, rate.TotalCostUsd);
        Assert.AreEqual(300m, rate.TotalSaleUsd);
        Assert.AreEqual(102000m, rate.TotalCostCrc);
        Assert.AreEqual(153000m, rate.TotalSaleCrc);
        Assert.AreEqual(300m, rate.TotalSaleAmount);
    }
}
