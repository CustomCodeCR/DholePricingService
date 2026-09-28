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
            rateCode: "QUO-A7K2P-9X4M8Q",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(),
            agentName: "Agente",
            agentCode: "AGT",
            carrierId: Guid.NewGuid(),
            carrierName: "Naviera",
            carrierCode: "CAR",
            polId: Guid.NewGuid(),
            polName: "Shanghai",
            polCode: "CNSHA",
            poeId: Guid.NewGuid(),
            poeName: "Caldera, Costa Rica",
            poeCode: "CRCAL",
            podId: null,
            podName: null,
            podCode: null,
            containerTypeId: Guid.NewGuid(),
            containerTypeName: "40 HC",
            containerTypeCode: "40HC",
            incotermId: null,
            incotermName: null,
            incotermCode: null,
            currencyId: Guid.NewGuid(),
            currencyName: "USD",
            currencyCode: "USD",
            freeDays: 0,
            validFrom: DateTime.UtcNow.Date,
            validTo: DateTime.UtcNow.Date.AddDays(30),
            containerQuantity: 1,
            clientName: null,
            idtraNumber: null,
            quoNumber: null,
            includes: null,
            subjectTo: null,
            excludes: null,
            transitTime: null,
            rateType: RateType.Spot,
            createdBy: null
        );
        rate.ConfigureExchangeRateSnapshot(500m, 510m, 510m, DateTime.UtcNow.Date, DateTime.UtcNow, "Test", false, null);
        rate.AddRateDetail(rate.Id, null, "Freight", CostDetailType.Freight, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "USD", "USD", 100m, 150m, null, 1m, null);
        rate.AddRateDetail(rate.Id, null, "Aduanas", CostDetailType.CustomsCharge, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 51000m, 76500m, null, 1m, null);

        rate.SetAmounts(null);

        Assert.AreEqual(200m, rate.TotalCostUsd);
        Assert.AreEqual(300m, rate.TotalSaleUsd);
        Assert.AreEqual(102000m, rate.TotalCostCrc);
        Assert.AreEqual(153000m, rate.TotalSaleCrc);
        Assert.AreEqual(300m, rate.TotalSaleAmount);
    }
}
