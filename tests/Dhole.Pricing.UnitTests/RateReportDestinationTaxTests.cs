using System.Text.Json;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportDestinationTaxTests
{
    [TestMethod]
    public void QuoteWithoutSelectedVat_HidesVatAndDoesNotAddItToTotal()
    {
        var rate = CreateRate();
        var detail = AddLine(rate, "Cargos destino", 125m);
        detail.ConfigureDestinationTax(true, 13m);
        detail.ConfigureDestinationTax(false, 0m);

        using var data = Report(rate);
        Assert.IsFalse(data.RootElement.GetProperty("rate").GetProperty("hasDestinationTax").GetBoolean());
        var noVatItem = data.RootElement.GetProperty("items")[0];
        Assert.AreEqual(string.Empty, noVatItem.GetProperty("destinationTaxLabel").GetString());
        Assert.AreEqual(0m, noVatItem.GetProperty("lineTaxAmount").GetDecimal());
        Assert.AreEqual(125m, noVatItem.GetProperty("lineTotalWithTaxAmount").GetDecimal());

        var total = data.RootElement.GetProperty("currencyTotals")[0];
        Assert.AreEqual(125m, total.GetProperty("subtotalAmount").GetDecimal());
        Assert.AreEqual(0m, total.GetProperty("taxAmount").GetDecimal());
        Assert.AreEqual(125m, total.GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void QuoteWithSelectedVat_ShowsLineTaxAndIncludesItOnlyOnce()
    {
        var rate = CreateRate();
        AddLine(rate, "Flete sin IVA", 100m);
        var taxed = AddLine(rate, "Anticipado", 125m);
        taxed.ConfigureDestinationTax(true, 13m);

        using var data = Report(rate);
        Assert.IsTrue(data.RootElement.GetProperty("rate").GetProperty("hasDestinationTax").GetBoolean());
        var item = data.RootElement.GetProperty("items").EnumerateArray()
            .Single(value => value.GetProperty("description").GetString() == "Anticipado");
        StringAssert.Contains(item.GetProperty("destinationTaxLabel").GetString()!, "IVA 13%: USD 16.25");
        Assert.AreEqual(125m, item.GetProperty("lineTotalAmount").GetDecimal());
        Assert.AreEqual(16.25m, item.GetProperty("lineTaxAmount").GetDecimal());
        Assert.AreEqual(141.25m, item.GetProperty("lineTotalWithTaxAmount").GetDecimal());

        var untaxed = data.RootElement.GetProperty("items").EnumerateArray()
            .Single(value => value.GetProperty("description").GetString() == "Flete sin IVA");
        Assert.AreEqual(0m, untaxed.GetProperty("lineTaxAmount").GetDecimal());
        Assert.AreEqual(100m, untaxed.GetProperty("lineTotalWithTaxAmount").GetDecimal());

        var total = data.RootElement.GetProperty("currencyTotals")[0];
        Assert.AreEqual(225m, total.GetProperty("subtotalAmount").GetDecimal());
        Assert.AreEqual(16.25m, total.GetProperty("taxAmount").GetDecimal());
        Assert.AreEqual(241.25m, total.GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void QuoteAllIn_PreservesSubtotalAndAddsSelectedVatToPayableTotal()
    {
        var rate = CreateRate();
        rate.ConfigureCommercialPresentation(true);
        var taxed = AddLine(rate, "Cargos destino", 125m);
        taxed.ConfigureDestinationTax(true, 13m);

        using var data = Report(rate);
        var item = data.RootElement.GetProperty("items")[0];
        Assert.AreEqual("ALL IN", item.GetProperty("description").GetString());
        Assert.AreEqual(125m, item.GetProperty("lineTotalAmount").GetDecimal());
        Assert.AreEqual(16.25m, item.GetProperty("lineTaxAmount").GetDecimal());
        Assert.AreEqual(141.25m, item.GetProperty("lineTotalWithTaxAmount").GetDecimal());
        var total = data.RootElement.GetProperty("currencyTotals")[0];
        Assert.AreEqual(125m, total.GetProperty("subtotalAmount").GetDecimal());
        Assert.AreEqual(16.25m, total.GetProperty("taxAmount").GetDecimal());
        Assert.AreEqual(141.25m, total.GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void QuoteWithEquivalentCurrencies_ConvertsSelectedVatInBothTotals()
    {
        var rate = CreateRate();
        rate.ConfigureExchangeRateSnapshot(
            purchase: 495m, sale: 500m, applied: 500m, rateDate: DateTime.UtcNow.Date,
            capturedAtUtc: DateTime.UtcNow, source: "Manual", manualOverride: true, updatedBy: null);
        AddLine(rate, "Flete USD sin IVA", 100m);
        var taxed = rate.AddRateDetail(
            rate.Id, null, "Cargos destino CRC",
            CostDetailType.DestinationCharge, CostType.Variable, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 0m, 1000m, null, 1m, null);
        taxed.ConfigureDestinationTax(true, 13m);

        using var data = Report(rate);
        var currencies = data.RootElement.GetProperty("currencyTotals").EnumerateArray().ToArray();
        Assert.AreEqual(2, currencies.Length);
        Assert.AreEqual(102m, currencies[0].GetProperty("subtotalAmount").GetDecimal());
        Assert.AreEqual(0.26m, currencies[0].GetProperty("taxAmount").GetDecimal());
        Assert.AreEqual(102.26m, currencies[0].GetProperty("amount").GetDecimal());
        Assert.AreEqual(51000m, currencies[1].GetProperty("subtotalAmount").GetDecimal());
        Assert.AreEqual(130m, currencies[1].GetProperty("taxAmount").GetDecimal());
        Assert.AreEqual(51130m, currencies[1].GetProperty("amount").GetDecimal());
    }


    [TestMethod]
    public void MixedFclQuote_LabelsCarrierLinkedCostsWithoutLabelingGeneralCharges()
    {
        var rate = CreateRate();
        rate.ReplaceContainerAllocations(
        [
            new RateContainerAllocationSpec(rate.ContainerTypeId, "40HC", "40HC", 1),
            new RateContainerAllocationSpec(Guid.NewGuid(), "20DV", "20DV", 1)
        ], null);
        rate.AddRateDetail(rate.Id, null, "Flete Internacional · 40HC",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerContainer,
            rate.CurrencyId, "USD", "USD", 6500m, 6500m,
            "Flete específico para 1 × 40HC · NavieraId: 11111111-1111-1111-1111-111111111111 · Naviera: COSCO",
            1m, null);
        rate.AddRateDetail(rate.Id, null, "Flete Internacional · 20DV",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerContainer,
            rate.CurrencyId, "USD", "USD", 6300m, 6300m,
            "Flete específico para 1 × 20DV · NavieraId: 22222222-2222-2222-2222-222222222222 · Naviera: MSC",
            1m, null);
        rate.AddRateDetail(rate.Id, null, "Cargos destino línea naviera",
            CostDetailType.DestinationCharge, CostType.Fixed, ChargeBasis.PerContainer,
            rate.CurrencyId, "USD", "USD", 275m, 290m,
            "THC · Naviera: COSCO · Equipo: 1 × 40HC", 1m, null);
        AddLine(rate, "Documentación", 50m);

        using var data = Report(rate);
        var items = data.RootElement.GetProperty("items").EnumerateArray().ToArray();
        StringAssert.Contains(
            items.Single(row => row.GetProperty("description").GetString()!.Contains("Cargos destino línea naviera"))
                .GetProperty("description").GetString()!,
            "Naviera: COSCO · Equipo: 1 × 40HC");
        StringAssert.Contains(
            items.Single(row => row.GetProperty("description").GetString()!.Contains("Flete Internacional · 20DV"))
                .GetProperty("description").GetString()!,
            "Naviera: MSC");
        Assert.AreEqual("Documentación",
            items.Single(row => row.GetProperty("description").GetString() == "Documentación")
                .GetProperty("description").GetString());
        Assert.AreEqual("COSCO / MSC", data.RootElement.GetProperty("rate").GetProperty("carrier").GetString());
        Assert.IsFalse(items.Any(row => row.GetProperty("notes").GetString()?.Contains("NavieraId:") == true));
    }

    [TestMethod]
    public void AllInWithoutExchangeRate_RendersSeparateCurrencies()
    {
        var rate = CreateRate();
        rate.ConfigureCommercialPresentation(true);
        AddLine(rate, "Flete USD", 100m);
        rate.AddRateDetail(rate.Id, null, "Cargos destino CRC",
            CostDetailType.DestinationCharge, CostType.Variable, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 0m, 1000m, null, 1m, null);

        using var data = Report(rate);
        Assert.IsFalse(data.RootElement.GetProperty("rate").GetProperty("useAllInPresentation").GetBoolean());
        Assert.IsTrue(data.RootElement.GetProperty("rate").GetProperty("allInFallbackToDetails").GetBoolean());
        Assert.AreEqual(2, data.RootElement.GetProperty("items").GetArrayLength());
        var totals = data.RootElement.GetProperty("currencyTotals").EnumerateArray().ToArray();
        Assert.AreEqual(100m, totals.Single(x => x.GetProperty("currencyCode").GetString() == "USD")
            .GetProperty("amount").GetDecimal());
        Assert.AreEqual(1000m, totals.Single(x => x.GetProperty("currencyCode").GetString() == "CRC")
            .GetProperty("amount").GetDecimal());
    }

    [TestMethod]
    public void AllInWithExchangeRate_ConvertsAndKeepsSingleLine()
    {
        var rate = CreateRate();
        rate.ConfigureCommercialPresentation(true);
        rate.ConfigureExchangeRateSnapshot(
            purchase: 495m, sale: 500m, applied: 500m, rateDate: DateTime.UtcNow.Date,
            capturedAtUtc: DateTime.UtcNow, source: "Manual", manualOverride: true, updatedBy: null);
        AddLine(rate, "Flete USD", 100m);
        rate.AddRateDetail(rate.Id, null, "Cargos destino CRC",
            CostDetailType.DestinationCharge, CostType.Variable, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 0m, 1000m, null, 1m, null);

        using var data = Report(rate);
        Assert.IsTrue(data.RootElement.GetProperty("rate").GetProperty("useAllInPresentation").GetBoolean());
        Assert.IsFalse(data.RootElement.GetProperty("rate").GetProperty("allInFallbackToDetails").GetBoolean());
        var items = data.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual("ALL IN", items[0].GetProperty("description").GetString());
        Assert.AreEqual(102m, items[0].GetProperty("lineTotalAmount").GetDecimal());
    }

    [TestMethod]
    public void AllInWithHistoricalCurrencyCode_UsesCurrencyName()
    {
        var rate = CreateRate();
        rate.ConfigureCommercialPresentation(true);
        rate.AddRateDetail(rate.Id, null, "Flete código histórico",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "LEGACY", 0m, 150m, null, 1m, null);

        using var data = Report(rate);
        Assert.IsTrue(data.RootElement.GetProperty("rate").GetProperty("useAllInPresentation").GetBoolean());
        Assert.AreEqual(150m, data.RootElement.GetProperty("items")[0]
            .GetProperty("lineTotalAmount").GetDecimal());
    }

    private static RateDetail AddLine(RateHeader rate, string name, decimal sale) =>
        rate.AddRateDetail(
            rate.Id, null, name, CostDetailType.DestinationCharge, CostType.Variable,
            ChargeBasis.PerShipment, rate.CurrencyId, "USD", "USD",
            0m, sale, null, 1m, null);

    private static JsonDocument Report(RateHeader rate) =>
        JsonDocument.Parse(new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));

    private static RateHeader CreateRate() => RateHeader.Create(
        rateCode: "QUO-VAT-TEST", sourceImportFclRateId: null,
        agentId: Guid.NewGuid(), agentName: "Grupo Castro Fallas", agentCode: "GCF",
        carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
        polId: Guid.NewGuid(), polName: "Ningbo, China", polCode: "CNNGB",
        poeId: Guid.NewGuid(), poeName: "Caldera, Costa Rica", poeCode: "CRCAL",
        podId: null, podName: null, podCode: null,
        containerTypeId: Guid.NewGuid(), containerTypeName: "40 High Cube", containerTypeCode: "40HC",
        incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "FOB",
        currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
        freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(1),
        containerQuantity: 1, clientName: "Prueba IVA", idtraNumber: null, quoNumber: null,
        includes: null, subjectTo: null, excludes: null, transitTime: null,
        rateType: RateType.Spot, createdBy: null);
}
