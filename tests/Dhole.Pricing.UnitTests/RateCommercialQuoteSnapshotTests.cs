using System.Text.Json;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateCommercialQuoteSnapshotTests
{
    // Every transport mode and price basis consumes one persisted snapshot.
    // Neither the PDF nor the API should apply its own quantity algorithm.
    [DataTestMethod]
    [DataRow(ShipmentMode.Fcl, ChargeBasis.PerContainer)]
    [DataRow(ShipmentMode.Fcl, ChargeBasis.PerTeu)]
    [DataRow(ShipmentMode.Ftl, ChargeBasis.PerTruck)]
    [DataRow(ShipmentMode.Ltl, ChargeBasis.PerChargeableCbm)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerChargeableCft)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerKg)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.Per100Kg)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerLb)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerCbm)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerCft)]
    [DataRow(ShipmentMode.Fcl, ChargeBasis.PerShipment)]
    [DataRow(ShipmentMode.Ftl, ChargeBasis.PerService)]
    [DataRow(ShipmentMode.Fcl, ChargeBasis.PerDocument)]
    [DataRow(ShipmentMode.Ltl, ChargeBasis.PerPallet)]
    [DataRow(ShipmentMode.Lcl, ChargeBasis.PerPackage)]
    public void SavedLine_FinancialSnapshot_IsIdenticalToPdfForAnyChargeBasis(
        ShipmentMode mode, ChargeBasis basis)
    {
        var rate = CreateRate();
        rate.ConfigureShipment(mode, 3, 2, 54.885m, 0.248m, 500m, null, null);
        var line = rate.AddRateDetail(rate.Id, null, "Cargo calculado",
            CostDetailType.Other, CostType.Variable, basis,
            rate.CurrencyId, "USD", "USD", 3.50m, 7.25m, null, 2m, null);
        line.ConfigureDestinationTax(true, 13m);

        var quote = RateCommercialQuoteSnapshot.Create(rate);
        var expected = quote.For(line);
        Assert.AreEqual(expected.UnitSale * expected.Quantity, expected.SaleSubtotal);
        Assert.AreEqual(expected.SaleSubtotal + expected.TaxAmount, expected.SaleTotal);
        Assert.AreEqual(expected.SaleSubtotal - expected.CostSubtotal, expected.UtilitySubtotal);

        using var doc = JsonDocument.Parse(
            new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));
        var printed = doc.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.AreEqual(expected.Quantity, printed.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(expected.UnitSale, printed.GetProperty("unitSaleAmount").GetDecimal());
        Assert.AreEqual(expected.SaleSubtotal, printed.GetProperty("lineTotalAmount").GetDecimal());
        Assert.AreEqual(expected.TaxAmount, printed.GetProperty("lineTaxAmount").GetDecimal());
        Assert.AreEqual(expected.SaleTotal, printed.GetProperty("lineTotalWithTaxAmount").GetDecimal());
        Assert.AreEqual(expected.SaleTotal,
            doc.RootElement.GetProperty("currencyTotals")[0].GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void ZeroSale_CannotAppearInPdf_ButRemainsInPersistedExcelProjection()
    {
        var rate = CreateRate();
        rate.AddRateDetail(rate.Id, null, "BL sin venta",
            CostDetailType.Documentation, CostType.Fixed, ChargeBasis.PerDocument,
            rate.CurrencyId, "USD", "USD", 10m, 0m, null, 1m, null);
        rate.AddRateDetail(rate.Id, null, "Trámite",
            CostDetailType.CustomsCharge, CostType.Variable, ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 5m, 90m, null, 1m, null);

        using var doc = JsonDocument.Parse(
            new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(1, items.Length);
        Assert.AreEqual("Trámite", items[0].GetProperty("description").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("rows").GetArrayLength());
        Assert.AreEqual(90m, doc.RootElement.GetProperty("currencyTotals")[0]
            .GetProperty("amount").GetDecimal());
    }

    private static RateHeader CreateRate() => RateHeader.Create(
        rateCode: "QUOTE-PARITY", sourceImportFclRateId: null,
        agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AG",
        carrierId: Guid.NewGuid(), carrierName: "Proveedor", carrierCode: "PR",
        polId: Guid.NewGuid(), polName: "Origen", polCode: "ORG",
        poeId: Guid.NewGuid(), poeName: "Destino", poeCode: "DST",
        podId: null, podName: null, podCode: null,
        containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
        incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "FOB",
        currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
        freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(1),
        containerQuantity: 1, clientName: "Prueba de todas las bases", idtraNumber: null, quoNumber: null,
        includes: null, subjectTo: null, excludes: null, transitTime: null,
        rateType: RateType.Spot, createdBy: null);
}
