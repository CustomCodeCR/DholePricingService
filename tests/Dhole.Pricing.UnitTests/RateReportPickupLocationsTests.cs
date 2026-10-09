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
    public void MiamiOwnLclPdf_IncludesPersistedCatalogPickupAndOtherPricedLines()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
        rate.ConfigureShipment(
            ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 171m, totalVolumeCbm: 0.16m,
            kgPerCbm: 500m, cargoLinesJson: null, updatedBy: null);
        rate.ConfigurePickupLocations(
            """[{"Address":"7373 Hunt Ave, Garden Grove, CA","CargoCondition":"NationalizedCargo"}]""");

        rate.AddRateDetail(
            rate.Id, null, "Flete Miami → Costa Rica",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Freight,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, "USD", "USD", 20m, 24.84m,
            "LCL PROPIO · Plan Miami", 12.077m, null);
        rate.AddRateDetail(
            rate.Id, Guid.NewGuid(), "Recolecta",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.InlandTransport,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerChargeableCft,
            rate.CurrencyId, "USD", "USD", 630m, 655m, null, 12.077m, null);
        rate.AddRateDetail(
            rate.Id, Guid.NewGuid(), "Forwarding",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.OriginCharge,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 40m, 50m, null, 1m, null);
        rate.AddRateDetail(
            rate.Id, null, "Manejos",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.OriginCharge,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 55m, 65m, null, 1m, null);

        using var report = QuoteData(rate);
        var rows = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(4, rows.Length, "No se pueden ocultar líneas cotizadas por tener CostId.");
        var pickup = rows.Single(item => item.GetProperty("description").GetString() == "Recolecta");
        Assert.AreEqual(1m, pickup.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(655m, pickup.GetProperty("lineTotalAmount").GetDecimal());
        Assert.IsTrue(rows.Any(item => item.GetProperty("description").GetString() == "Forwarding"));
        Assert.IsTrue(rows.Any(item => item.GetProperty("description").GetString() == "Manejos"));
        Assert.AreEqual(4, report.RootElement.GetProperty("rows").GetArrayLength());
        Assert.AreEqual(1, report.RootElement.GetProperty("rate")
            .GetProperty("pickupLocations").GetArrayLength());
    }

    [TestMethod]
    public void PdfSavedPickupWithZeroSale_RemainsVisibleWithoutInventingACharge()
    {
        var rate = CreateRate("EXW", "Miami, Estados Unidos", "USMIA");
        rate.ConfigureShipment(
            ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 171m, totalVolumeCbm: 0.16m,
            kgPerCbm: 500m, cargoLinesJson: null, updatedBy: null);
        rate.AddRateDetail(
            rate.Id, Guid.NewGuid(), "Recolecta",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.InlandTransport,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 0m, 0m, null, 1m, null);
        rate.AddRateDetail(
            rate.Id, null, "Otro cargo por completar",
            Dhole.Pricing.Domain.Costs.Enums.CostDetailType.Other,
            Dhole.Pricing.Domain.Costs.Enums.CostType.Variable,
            Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 0m, 0m, null, 1m, null);

        using var report = QuoteData(rate);
        var items = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(1, items.Length);
        Assert.AreEqual("Recolecta", items[0].GetProperty("description").GetString());
        Assert.AreEqual(1m, items[0].GetProperty("quantity").GetDecimal());
        Assert.AreEqual(0m, items[0].GetProperty("lineTotalAmount").GetDecimal());
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
