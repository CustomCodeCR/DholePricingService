using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateFreightQuantityTests
{
    [TestMethod]
    public void AddRateDetail_Freight_UsesRequestedContainerTypeQuantity()
    {
        var rate = CreateRate(containerQuantity: 3);

        var detail = rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: "Flete internacional · 40 HC",
            costDetailType: CostDetailType.Freight,
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

        Assert.AreEqual(1m, detail.Quantity);
        Assert.AreEqual(100m, rate.TotalCostAmount);
        Assert.AreEqual(125m, rate.TotalSaleAmount);
    }

    [TestMethod]
    public void AddRateDetail_InlandTransport_StillUsesTotalContainerQuantity()
    {
        var rate = CreateRate(containerQuantity: 3);

        var detail = rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: "Flete terrestre",
            costDetailType: CostDetailType.InlandTransport,
            costType: CostType.Variable,
            currencyId: rate.CurrencyId,
            currencyName: rate.CurrencyName,
            currencyCode: rate.CurrencyCode,
            costAmount: 50m,
            saleAmount: 60m,
            notes: null,
            quantity: 1,
            updatedBy: null
        );

        rate.SetAmounts(null);

        Assert.AreEqual(3m, detail.Quantity);
        Assert.AreEqual(150m, rate.TotalCostAmount);
        Assert.AreEqual(180m, rate.TotalSaleAmount);
    }

    [TestMethod]
    public void MixedContainers_CanHaveDifferentInternationalFreightPerType()
    {
        var rate = CreateRate(containerQuantity: 2);
        var container20 = Guid.NewGuid();
        var container40 = Guid.NewGuid();

        rate.ReplaceContainerAllocations(
            [
                new RateContainerAllocationSpec(container40, "40 HC", "40HC", 1),
                new RateContainerAllocationSpec(container20, "20 DV", "20DV", 1),
            ],
            updatedBy: null
        );

        rate.AddRateDetail(
            rate.Id, null, "Flete internacional · 40 HC", CostDetailType.Freight, CostType.Variable,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, 7000m, 7900m, null, 1, null
        );
        rate.AddRateDetail(
            rate.Id, null, "Flete internacional · 20 DV", CostDetailType.Freight, CostType.Variable,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, 5000m, 5600m, null, 1, null
        );

        rate.SetAmounts(null);

        Assert.AreEqual(12000m, rate.TotalCostAmount);
        Assert.AreEqual(13500m, rate.TotalSaleAmount);
    }

    [TestMethod]
    public void UpdateContainerQuantity_DoesNotOverwriteExplicitOceanFreightQuantity()
    {
        var rate = CreateRate(containerQuantity: 1);
        var maritime = rate.AddRateDetail(
            rate.Id, null, "Flete internacional · 40 HC", CostDetailType.Freight, CostType.Variable,
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
            rate.IncotermId, rate.IncotermName, rate.IncotermCode,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            rate.FreeDays, rate.ValidFrom, rate.ValidTo,
            4,
            rate.ClientName, rate.IdtraNumber, rate.QuoNumber, rate.Includes, rate.SubjectTo,
            rate.Excludes, rate.TransitTime, rate.RateType, updatedBy: null
        );
        rate.SetAmounts(null);

        Assert.AreEqual(1m, maritime.Quantity);
        Assert.AreEqual(4m, land.Quantity);
        Assert.AreEqual(300m, rate.TotalCostAmount);
        Assert.AreEqual(365m, rate.TotalSaleAmount);
    }

    [TestMethod]
    public void ReplaceContainerAllocations_AllowsMixedTypesAndKeepsTotalQuantity()
    {
        var rate = CreateRate(containerQuantity: 2);
        var container20 = Guid.NewGuid();
        var container40 = Guid.NewGuid();

        rate.ReplaceContainerAllocations(
            [
                new RateContainerAllocationSpec(container40, "40 HC", "40HC", 1),
                new RateContainerAllocationSpec(container20, "20 DV", "20DV", 1),
            ],
            updatedBy: null
        );

        Assert.AreEqual(2, rate.ContainerQuantity);
        Assert.AreEqual(2, rate.RateContainers.Count);
        Assert.IsTrue(rate.RateContainers.Any(x => x.ContainerTypeId == container40 && x.Quantity == 1));
        Assert.IsTrue(rate.RateContainers.Any(x => x.ContainerTypeId == container20 && x.Quantity == 1));
        StringAssert.Contains(rate.RateName, "1 x 20 DV");
        StringAssert.Contains(rate.RateName, "1 x 40 HC");
    }

    [TestMethod]
    [DataRow(CostDetailType.Freight)]
    [DataRow(CostDetailType.InlandTransport)]
    public void CreateCost_FreightTypes_AreAlwaysMarkedPerContainer(CostDetailType detailType)
    {
        var cost = Cost.Create(
            "Flete", CostType.Fixed, detailType,
            carrierId: null, carrierName: null, carrierCode: null,
            agentId: null, agentName: null, agentCode: null,
            portId: null, portName: null, portCode: null, portRole: null,
            polId: null, polName: null, polCode: null,
            poeId: null, poeName: null, poeCode: null,
            podId: null, podName: null, podCode: null,
            incoterms: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            costAmount: 100m, saleAmount: 120m, notes: null,
            isAccountant: false, createdBy: null
        );

        Assert.IsTrue(cost.IsAccountant);
    }

    [TestMethod]
    public void CreateCost_LegacyPerContainerFlag_PreservesPerContainerChargeBasis()
    {
        var cost = Cost.Create(
            "Handling", CostType.Fixed, CostDetailType.DestinationCharge,
            carrierId: null, carrierName: null, carrierCode: null,
            agentId: null, agentName: null, agentCode: null,
            portId: null, portName: null, portCode: null, portRole: null,
            polId: null, polName: null, polCode: null,
            poeId: null, poeName: null, poeCode: null,
            podId: null, podName: null, podCode: null,
            incoterms: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            costAmount: 100m, saleAmount: 120m, notes: null,
            isAccountant: true, createdBy: null
        );

        Assert.AreEqual(ChargeBasis.PerContainer, cost.ChargeBasis);
        Assert.AreEqual(ShipmentMode.Fcl, cost.ShipmentMode);
        Assert.IsTrue(cost.IsAccountant);
    }

    [TestMethod]
    public void CreateCost_LegacyUniqueDocumentation_DefaultsToPerDocument()
    {
        var cost = Cost.Create(
            "BL", CostType.Fixed, CostDetailType.Documentation,
            carrierId: null, carrierName: null, carrierCode: null,
            agentId: null, agentName: null, agentCode: null,
            portId: null, portName: null, portCode: null, portRole: null,
            polId: null, polName: null, polCode: null,
            poeId: null, poeName: null, poeCode: null,
            podId: null, podName: null, podCode: null,
            incoterms: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            costAmount: 75m, saleAmount: 95m, notes: null,
            isAccountant: false, createdBy: null
        );

        Assert.AreEqual(ChargeBasis.PerDocument, cost.ChargeBasis);
        Assert.IsNull(cost.ShipmentMode);
        Assert.IsFalse(cost.IsAccountant);
    }

    [TestMethod]
    public void Lcl_UsesChargeableCbmForMetricBasedFreight()
    {
        var rate = CreateRate(containerQuantity: 1);
        rate.ConfigureShipment(
            ShipmentMode.Lcl,
            totalPackages: 4,
            totalPallets: 1,
            totalWeightKg: 800m,
            totalVolumeCbm: 1.2m,
            kgPerCbm: 500m,
            cargoLinesJson: null,
            updatedBy: null
        );

        var detail = rate.AddRateDetail(
            rate.Id, null, "Flete LCL", CostDetailType.Freight, CostType.Variable,
            ChargeBasis.PerChargeableCbm,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            100m, 125m, null, 1m, null
        );
        rate.SetAmounts(null);

        Assert.AreEqual(1.6m, rate.ChargeableQuantity);
        Assert.AreEqual(1.6m, detail.Quantity);
        Assert.AreEqual(160m, rate.TotalCostAmount);
        Assert.AreEqual(200m, rate.TotalSaleAmount);
        StringAssert.Contains(rate.RateName, "LCL");
    }

    [TestMethod]
    public void RateName_UsesIncotermDisplayValueBeforeCatalogCode()
    {
        var today = DateTime.UtcNow.Date;
        var rate = RateHeader.Create(
            rateCode: "QUO-2FQXC-PJ8YDL",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AGT",
            carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
            polId: Guid.NewGuid(), polName: "Ningbo, China", polCode: "CNNGB",
            poeId: Guid.NewGuid(), poeName: "Puerto Caldera", poeCode: "CRCAL",
            podId: Guid.NewGuid(), podName: "GAM", podCode: "CRGAM",
            containerTypeId: Guid.NewGuid(), containerTypeName: "40 DV", containerTypeCode: "40DV",
            incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "INC-2026-004",
            currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
            freeDays: 7, validFrom: today, validTo: today.AddDays(30),
            containerQuantity: 1, clientName: "Cliente Prueba", idtraNumber: null, quoNumber: null,
            includes: null, subjectTo: null, excludes: null, transitTime: "20 días", rateType: RateType.Tariff, createdBy: null
        );

        Assert.AreEqual(
            "QUO-2FQXC-PJ8YDL - Tarifa 1 x 40 DV - FOB - Ningbo, China To GAM Via Caldera - Cliente Prueba",
            rate.RateName
        );
        Assert.IsFalse(rate.RateName.Contains("INC-2026-004", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateCost_WithPolAndPoe_KeepsStructuredRouteCondition()
    {
        var polId = Guid.NewGuid();
        var poeId = Guid.NewGuid();

        var cost = Cost.Create(
            "Ruta Shanghai-Caldera", CostType.Fixed, CostDetailType.PortCharge,
            carrierId: null, carrierName: null, carrierCode: null,
            agentId: null, agentName: null, agentCode: null,
            portId: null, portName: null, portCode: null, portRole: null,
            polId: polId, polName: "Shanghai", polCode: "CNSHA",
            poeId: poeId, poeName: "Puerto Caldera", poeCode: "CRCAL",
            podId: null, podName: null, podCode: null,
            incoterms: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            costAmount: 100m, saleAmount: 120m, notes: null,
            isAccountant: false, createdBy: null
        );

        Assert.AreEqual(polId, cost.PolId);
        Assert.AreEqual(poeId, cost.PoeId);
        Assert.IsNull(cost.PodId);
        Assert.IsNull(cost.PortId);
        Assert.IsNull(cost.PortRole);
    }

    private static RateHeader CreateRate(int containerQuantity)
    {
        var today = DateTime.UtcNow.Date;
        return RateHeader.Create(
            rateCode: "QUO-A7K2P-9X4M8Q",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AGT",
            carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
            polId: Guid.NewGuid(), polName: "Shanghai", polCode: "CNSHA",
            poeId: Guid.NewGuid(), poeName: "Caldera", poeCode: "CRCAL",
            podId: Guid.NewGuid(), podName: "San José", podCode: "CRSJO",
            containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
            incotermId: null, incotermName: null, incotermCode: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            freeDays: 7, validFrom: today, validTo: today.AddDays(30),
            containerQuantity: containerQuantity, clientName: null, idtraNumber: null, quoNumber: null,
            includes: null, subjectTo: null, excludes: null, transitTime: "20 días", rateType: RateType.Tariff, createdBy: null
        );
    }
}
