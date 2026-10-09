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
    public void MiamiLclReport_LegacyOneCbmFreight_UsesActualCftInPdf()
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
            3m, 24.84m, "LCL PROPIO · Plan Miami: D", 35.3146667m, null
        );
        using var report = QuoteData(rate);
        var items = report.RootElement.GetProperty("items");
        var freight = items.EnumerateArray().First(item =>
            item.GetProperty("description").GetString() == "Flete Miami → Costa Rica");
        var chargeableCft = 171m / 14.16m;

        Assert.AreEqual(chargeableCft, freight.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(chargeableCft * 24.84m, freight.GetProperty("lineTotalAmount").GetDecimal());
        StringAssert.Contains(report.RootElement.GetProperty("rate")
            .GetProperty("containerSummary").GetString()!, "CFT cobrable");
    }

    [TestMethod]
    public void PdfTotals_UsePersistedFxRateToShowOneEquivalentAmountInUsdAndCrc()
    {
        var rate = CreateRate("EXW");
        rate.ConfigureExchangeRateSnapshot(
            purchase: null, sale: null, applied: 500m,
            rateDate: new DateTime(2026, 10, 9),
            capturedAtUtc: DateTime.UtcNow, source: "Test",
            manualOverride: true, updatedBy: null);
        rate.AddRateDetail(
            rate.Id, null, "Flete", Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 0m, 100m, null, 1m, null);
        rate.AddRateDetail(
            rate.Id, null, "DUA", Dhole.Pricing.Domain.Costs.Enums.CostDetailType.CustomsCharge,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 0m, 36000m, null, 1m, null);

        using var report = QuoteData(rate);
        var root = report.RootElement;
        var totals = root.GetProperty("currencyTotals");
        Assert.IsTrue(root.GetProperty("rate").GetProperty("hasEquivalentCurrencies").GetBoolean());
        Assert.AreEqual(2, totals.GetArrayLength());
        Assert.AreEqual("USD", totals[0].GetProperty("currencyCode").GetString());
        Assert.AreEqual(172m, totals[0].GetProperty("amount").GetDecimal());
        Assert.AreEqual("CRC", totals[1].GetProperty("currencyCode").GetString());
        Assert.AreEqual(86000m, totals[1].GetProperty("amount").GetDecimal());
        StringAssert.Contains(root.GetProperty("rate").GetProperty("exchangeRateNote").GetString()!, "500.0000");
    }

    [TestMethod]
    public void PdfPickup_IsAlwaysOneFlatChargeEvenWhenInputUsesCft()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
        rate.ConfigureShipment(
            ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 171m, totalVolumeCbm: 0.16m,
            kgPerCbm: 500m, cargoLinesJson: null, updatedBy: null);
        var pickup = rate.AddRateDetail(
            rate.Id, null, "Recolecta",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.InlandTransport,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, "USD", "USD", 630m, 655m, null, 12.077m, null);
        Assert.AreEqual(Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment, pickup.ChargeBasis);
        Assert.AreEqual(1m, pickup.Quantity);

        using var report = QuoteData(rate);
        var item = report.RootElement.GetProperty("items")[0];
        Assert.AreEqual(1m, item.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(655m, item.GetProperty("lineTotalAmount").GetDecimal());
        Assert.AreEqual("5.650 CFT", report.RootElement.GetProperty("rate").GetProperty("totalVolume").GetString());
    }

    [TestMethod]
    public void PdfCbmShipment_DoesNotPrintCftVolume()
    {
        var rate = CreateRate("EXW");
        rate.ConfigureShipment(
            ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 171m, totalVolumeCbm: 0.16m,
            kgPerCbm: 500m, cargoLinesJson: null, updatedBy: null);
        rate.AddRateDetail(
            rate.Id, null, "Flete",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCbm,
            rate.CurrencyId, "USD", "USD", 0m, 100m, null, 1m, null);
        using var report = QuoteData(rate);
        var header = report.RootElement.GetProperty("rate");
        Assert.AreEqual("0.160 CBM", header.GetProperty("totalVolume").GetString());
        StringAssert.Contains(header.GetProperty("containerSummary").GetString()!, "CBM cobrable");
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
