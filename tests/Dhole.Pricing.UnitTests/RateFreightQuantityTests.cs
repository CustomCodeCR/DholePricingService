using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateFreightQuantityTests
{
    [DataTestMethod]
    [DataRow(CostDetailType.Freight)]
    [DataRow(CostDetailType.InlandTransport)]
    public void AddRateDetail_FreightTypes_AlwaysUseContainerQuantity(CostDetailType detailType)
    {
        var rate = CreateRate(containerQuantity: 3);

        var detail = rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: detailType == CostDetailType.Freight ? "Flete marítimo" : "Flete terrestre",
            costDetailType: detailType,
            costType: CostType.Variable,
            currencyId: rate.CurrencyId,
            currencyName: rate.CurrencyName,
            currencyCode: rate.CurrencyCode,
            costAmount: 100m,
            saleAmount: 125m,
            notes: null,
            quantity: 1,
            updatedBy: null
        );

        rate.SetAmounts(null);

        Assert.AreEqual(3, detail.Quantity);
        Assert.AreEqual(300m, rate.TotalCostAmount);
        Assert.AreEqual(375m, rate.TotalSaleAmount);
    }

    [TestMethod]
    public void UpdateContainerQuantity_RecalculatesExistingMaritimeAndLandFreight()
    {
        var rate = CreateRate(containerQuantity: 1);
        var maritime = rate.AddRateDetail(
            rate.Id, null, "Flete marítimo", CostDetailType.Freight, CostType.Variable,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, 100m, 125m, null, 1, null
        );
        var land = rate.AddRateDetail(
            rate.Id, null, "Flete terrestre", CostDetailType.InlandTransport, CostType.Variable,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, 50m, 60m, null, 1, null
        );

        rate.Update(
            rate.AgentId!.Value, rate.AgentName!, rate.AgentCode!,
            rate.CarrierId!.Value, rate.CarrierName!, rate.CarrierCode!,
            rate.PolId, rate.PolName, rate.PolCode,
            rate.PoeId, rate.PoeName, rate.PoeCode,
            rate.PodId, rate.PodName, rate.PodCode,
            rate.ContainerTypeId, rate.ContainerTypeName, rate.ContainerTypeCode,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            rate.FreeDays, rate.ValidFrom, rate.ValidTo,
            4,
            rate.ClientName, rate.IdtraNumber, rate.QuoNumber, rate.Includes, rate.SubjectTo,
            rate.Excludes, rate.TransitDays, updatedBy: null
        );
        rate.SetAmounts(null);

        Assert.AreEqual(4, maritime.Quantity);
        Assert.AreEqual(4, land.Quantity);
        Assert.AreEqual(600m, rate.TotalCostAmount);
        Assert.AreEqual(740m, rate.TotalSaleAmount);
    }

    [DataTestMethod]
    [DataRow(CostDetailType.Freight)]
    [DataRow(CostDetailType.InlandTransport)]
    public void CreateCost_FreightTypes_AreAlwaysMarkedPerContainer(CostDetailType detailType)
    {
        var cost = Cost.Create(
            "Flete", CostType.Fixed, detailType,
            carrierId: null, carrierName: null, carrierCode: null,
            agentId: null, agentName: null, agentCode: null,
            portId: null, portName: null, portCode: null, portRole: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            costAmount: 100m, saleAmount: 120m, notes: null,
            isAccountant: false, createdBy: null
        );

        Assert.IsTrue(cost.IsAccountant);
    }

    private static RateHeader CreateRate(int containerQuantity)
    {
        var today = DateTime.UtcNow.Date;
        return RateHeader.Create(
            rateConsecutive: 1,
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AGT",
            carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
            polId: Guid.NewGuid(), polName: "Shanghai", polCode: "CNSHA",
            poeId: Guid.NewGuid(), poeName: "Caldera", poeCode: "CRCAL",
            podId: Guid.NewGuid(), podName: "San José", podCode: "CRSJO",
            containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            freeDays: 7, validFrom: today, validTo: today.AddDays(30),
            containerQuantity: containerQuantity, clientName: null, idtraNumber: null, quoNumber: null,
            includes: null, subjectTo: null, excludes: null, transitDays: 20, createdBy: null
        );
    }
}
