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
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
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

    [TestMethod]
    public void MiamiLclPdf_LegacyOneCbmFloor_RecomputesCftLineAndTotals()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
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
        var freight = rate.AddRateDetail(
            rate.Id, null, "Flete Miami → Costa Rica",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            3m, 24.84m, "LCL PROPIO · Plan Miami: D",
            35.3146667m, null
        );
        rate.AddRateDetail(
            rate.Id, null, "Manejos",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.DestinationCharge,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            0m, 65m, null, 1m, null
        );
        var billableCft = 171m / 14.16m;
        using var report = QuoteData(rate);
        var root = report.RootElement;
        var quote = root.GetProperty("rate");
        var items = root.GetProperty("items");
        var freightItem = items.EnumerateArray().First(item =>
            item.GetProperty("description").GetString() == "Flete Miami → Costa Rica");

        Assert.AreEqual(35.3146667m, freight.Quantity); // Commercial snapshot is untouched.
        Assert.AreEqual(billableCft, freightItem.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(billableCft * 24.84m, freightItem.GetProperty("lineTotalAmount").GetDecimal());
        StringAssert.Contains(quote.GetProperty("containerSummary").GetString()!, "CFT cobrable");
        Assert.AreEqual(billableCft / 35.3146667m, quote.GetProperty("chargeableQuantity").GetDecimal());
        var usd = root.GetProperty("currencyTotals").EnumerateArray().First();
        Assert.AreEqual(billableCft * 24.84m + 65m, usd.GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void MiamiLclPdf_Little_UsesOnlyVolumeAndIgnoresWeight()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
        rate.ConfigureShipment(
            ShipmentMode.Lcl,
            totalPackages: 1,
            totalPallets: 1,
            totalWeightKg: 171m,
            totalVolumeCbm: 0.16m,
            kgPerCbm: 500m,
            cargoLinesJson: """[{"Description":"Caja","Packages":1,"Pallets":1,"WeightKg":171,"LengthCm":100,"WidthCm":100,"HeightCm":16,"VolumeCbm":0.16,"IsStackable":true,"BillableVolumeCbm":0.2,"DeadSpaceCbm":0.04}]""",
            updatedBy: null
        );
        rate.AddRateDetail(
            rate.Id, null, "Flete Miami → Costa Rica",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            0m, 10m, "LCL PROPIO · Plan Miami: LITTLE",
            35.3146667m, null
        );
        using var report = QuoteData(rate);
        var freight = report.RootElement.GetProperty("items")[0];
        Assert.AreEqual(0.2m * 35.3146667m, freight.GetProperty("quantity").GetDecimal());
    }

    [TestMethod]
    public void NonMiamiLclPdf_DoesNotRewriteExistingCftQuantities()
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
            rate.Id, null, "Flete aéreo",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            0m, 10m, null,
            35.3146667m, null
        );
        using var report = QuoteData(rate);
        Assert.AreEqual(35.3146667m, report.RootElement.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
    }

    [TestMethod]
    public void MiamiAirLclPdf_DoesNotApplyMaritimeMatrixWeightRule()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
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
            rate.Id, null, "Flete aéreo",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            0m, 10m, null, 35.3146667m, null
        );
        using var report = QuoteData(rate);
        Assert.AreEqual(35.3146667m, report.RootElement.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
    }

    private static JsonDocument QuoteData(RateHeader rate)
    {
        var factory = new RateReportDataFactory(new ConfigurationBuilder().Build());
        return JsonDocument.Parse(factory.CreateDataJson(rate));
    }

    private static RateHeader CreateRate(string incoterm, string polName = "Colón, Panamá", string polCode = "PAONX")
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
            polName: polName,
            polCode: polCode,
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
