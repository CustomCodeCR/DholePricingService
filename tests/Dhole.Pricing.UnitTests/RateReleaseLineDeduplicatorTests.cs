using Dhole.Pricing.Application.Services;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReleaseLineDeduplicatorTests
{
    private static RateHeader CreateRate()
    {
        return RateHeader.Create(
            rateCode: "QUO-TEST-RELEASE",
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
            poeName: "Caldera",
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
    }

    [TestMethod]
    public void IdenticalReleaseWithDifferentCatalogIds_IsChargedOnce()
    {
        var rate = CreateRate();
        var currency = rate.CurrencyId;
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Release", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 75m, 0m, null, 1m, null);
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Release", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 75m, 0m, null, 1m, null);
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Manejos", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 0m, 55m, null, 1m, null);
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Manejos", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 50m, 0m, null, 1m, null);

        RateReleaseLineDeduplicator.RemoveExactDuplicates(rate, updatedBy: null);
        rate.SetAmounts(null);

        Assert.AreEqual(1, rate.RateDetails.Count(d => d.Name == "Release"));
        Assert.AreEqual(2, rate.RateDetails.Count(d => d.Name == "Manejos"));
        Assert.AreEqual(125m, rate.TotalCostUsd);
        Assert.AreEqual(55m, rate.TotalSaleUsd);
        Assert.IsTrue(RateReleaseLineDeduplicator.HasEquivalent(
            rate, "Release", ChargeBasis.PerShipment, "USD", 75m, 0m, null, 1m, null, false, 0m));
    }

    [TestMethod]
    public void ReleasesWithDifferentAmountsOrNotes_ArePreserved()
    {
        var rate = CreateRate();
        var currency = rate.CurrencyId;
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Release", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 75m, 0m, null, 1m, null);
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Release", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 90m, 0m, null, 1m, null);
        rate.AddRateDetail(rate.Id, Guid.NewGuid(), "Release", CostDetailType.DestinationCharge,
            CostType.Fixed, ChargeBasis.PerShipment, currency, "USD", "USD", 75m, 0m, "Otro servicio", 1m, null);

        RateReleaseLineDeduplicator.RemoveExactDuplicates(rate, updatedBy: null);

        Assert.AreEqual(3, rate.RateDetails.Count(d => d.Name == "Release"));
    }
}
