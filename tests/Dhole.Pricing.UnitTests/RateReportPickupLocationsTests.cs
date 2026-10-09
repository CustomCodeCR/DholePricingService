using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportPickupLocationsTests
{
    [TestMethod]
    public void Report_ListsAllExwPickupsInOrderWithClassification()
    {
        var rate = CreateRate("EXW");
        rate.ConfigurePickupLocation(null, "Dirección legacy", null, null);
        rate.ConfigurePickupLocations(
            """[{"Address":"Bodega Colón","CargoCondition":"FiscalCargo"},{"Address":"Parque logístico Panamá","CargoCondition":"NationalizedCargo"},{"Address":"Tercera ubicación"}]""");

        using var report = QuoteData(rate);
        var pickups = report.RootElement.GetProperty("rate").GetProperty("pickupLocations");

        Assert.AreEqual(3, pickups.GetArrayLength());
        Assert.AreEqual("Punto 1:", pickups[0].GetProperty("label").GetString());
        Assert.AreEqual("Bodega Colón", pickups[0].GetProperty("address").GetString());
        Assert.AreEqual(" — Carga fiscal", pickups[0].GetProperty("classification").GetString());
        Assert.AreEqual("Punto 2:", pickups[1].GetProperty("label").GetString());
        Assert.AreEqual(" — Carga nacionalizada", pickups[1].GetProperty("classification").GetString());
        Assert.AreEqual("Tercera ubicación", pickups[2].GetProperty("address").GetString());
        Assert.AreEqual("", pickups[2].GetProperty("classification").GetString());
    }

    [TestMethod]
    public void Report_UsesLegacyExwPickupWhenMultipleLocationsAreAbsent()
    {
        var rate = CreateRate("EXW");
        rate.ConfigurePickupLocation(null, "Zona Libre de Colón, Panamá", null, null);

        using var report = QuoteData(rate);
        var pickups = report.RootElement.GetProperty("rate").GetProperty("pickupLocations");

        Assert.AreEqual(1, pickups.GetArrayLength());
        Assert.AreEqual("Zona Libre de Colón, Panamá", pickups[0].GetProperty("address").GetString());
    }

    [TestMethod]
    public void Report_DoesNotConfuseFcaDeliveryAddressWithPickup()
    {
        var rate = CreateRate("FCA");
        rate.ConfigurePickupLocation(null, "Dirección de entrega FCA", null, null);

        using var report = QuoteData(rate);
        Assert.AreEqual(0, report.RootElement.GetProperty("rate")
            .GetProperty("pickupLocations").GetArrayLength());
    }

    [TestMethod]
    public void MiamiLclReport_UsesPersistedCftForHeaderAndFreightLine()
    {
        var rate = CreateRate("EXW");
        rate.ConfigureShipment(
            ShipmentMode.Lcl,
            totalPackages: 1,
            totalPallets: 1,
            totalWeightKg: 171m,
            totalVolumeCbm: 0.16m,
            kgPerCbm: 500m,
            cargoLinesJson: null,
            updatedBy: null
        );
        rate.AddRateDetail(
            rate.Id, null, "Flete Miami → Costa Rica",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            3m, 24.84m, null, 171m / 14.16m, null
        );

        using var report = QuoteData(rate);
        var root = report.RootElement;
        StringAssert.Contains(root.GetProperty("rate").GetProperty("containerSummary").GetString()!, "CFT cobrable");
        var freight = root.GetProperty("items")[0];
        Assert.AreEqual(171m / 14.16m, freight.GetProperty("quantity").GetDecimal());
        Assert.AreEqual((171m / 14.16m) * 24.84m,
            freight.GetProperty("lineTotalAmount").GetDecimal());
    }

    private static JsonDocument QuoteData(RateHeader rate)
    {
        var factory = new RateReportDataFactory(new ConfigurationBuilder().Build());
        return JsonDocument.Parse(factory.CreateDataJson(rate));
    }

    private static RateHeader CreateRate(string incoterm)
    {
        return RateHeader.Create(
            rateCode: "QUO-A7K2P-9X4M8Q",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(),
            agentName: "Grupo Castro Fallas",
            agentCode: "GCF",
            carrierId: Guid.NewGuid(),
            carrierName: "Naviera",
            carrierCode: "CAR",
            polId: Guid.NewGuid(),
            polName: "Colón, Panamá",
            polCode: "PAONX",
            poeId: Guid.NewGuid(),
            poeName: "Ciudad de Guatemala",
            poeCode: "GTGUA",
            podId: null,
            podName: null,
            podCode: null,
            containerTypeId: Guid.NewGuid(),
            containerTypeName: "LTL",
            containerTypeCode: "LTL",
            incotermId: Guid.NewGuid(),
            incotermName: incoterm,
            incotermCode: incoterm,
            currencyId: Guid.NewGuid(),
            currencyName: "USD",
            currencyCode: "USD",
            freeDays: 0,
            validFrom: DateTime.UtcNow.Date,
            validTo: DateTime.UtcNow.Date.AddDays(1),
            containerQuantity: 1,
            clientName: "Cliente prueba",
            idtraNumber: null,
            quoNumber: null,
            includes: null,
            subjectTo: null,
            excludes: null,
            transitTime: null,
            rateType: RateType.Spot,
            createdBy: null);
    }
}
