using System.Text.Json;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportMiamiFreightBreakdownTests
{
    [TestMethod]
    public void OwnMiamiClientA_DisplaysFreightBunkerThc_WithoutChangingSale()
    {
        var rate = CreateRate("Miami, Estados Unidos", "USMIA");
        rate.ConfigureShipment(ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 5m, totalVolumeCbm: 0.09m, kgPerCbm: 500m,
            cargoLinesJson: null, updatedBy: null);
        rate.AddRateDetail(rate.Id, null, "Flete Miami → Costa Rica",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerChargeableCft,
            rate.CurrencyId, "USD", "USD", 2.80m, 87.50m,
            "LCL PROPIO · Plan Miami: A · ConsolidadoId: 44444444-4444-4444-4444-444444444444",
            3.20m, null);
        rate.AddRateDetail(rate.Id, null, "SED",
            CostDetailType.Documentation, CostType.Fixed, ChargeBasis.PerShipment,
            rate.CurrencyId, "USD", "USD", 0m, 25m, "LCL PROPIO · Plan Miami: A", 1m, null);

        using var report = Report(rate);
        var items = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(2, items.Length);
        var freight = items.Single(item => item.GetProperty("description").GetString()!.StartsWith("Flete Miami"));
        var lines = freight.GetProperty("miamiBreakdown").GetString()!;
        StringAssert.Contains(lines, "Flete: USD 1.75/CFT");
        StringAssert.Contains(lines, "Bunker: USD 0.70/CFT");
        StringAssert.Contains(lines, "THC: USD 0.35/CFT");
        StringAssert.Contains(lines, "USD 5.60");
        StringAssert.Contains(lines, "USD 2.24");
        StringAssert.Contains(lines, "USD 1.12");
        Assert.AreEqual(280m, freight.GetProperty("lineTotalAmount").GetDecimal());
        Assert.AreEqual(string.Empty, items.Single(item => item.GetProperty("description").GetString() == "SED")
            .GetProperty("miamiBreakdown").GetString());
        Assert.AreEqual(305m, report.RootElement.GetProperty("currencyTotals")[0]
            .GetProperty("subtotalAmount").GetDecimal());
    }

    [TestMethod]
    public void NonMiamiAndColoader_DoNotDisplayInternalMiamiMatrix()
    {
        foreach (var (pol, code, notes) in new[]
        {
            ("Ningbo, China", "CNNGB", "LCL PROPIO · Plan Miami: A"),
            ("Miami, Estados Unidos", "USMIA", "LCL COLOADER"),
            ("Miami, Estados Unidos", "USMIA", "LCL PROPIO · Plan Miami: LITTLE")
        })
        {
            var rate = CreateRate(pol, code);
            rate.ConfigureShipment(ShipmentMode.Lcl, 1, 1, 5m, 0.09m, 500m, null, null);
            rate.AddRateDetail(rate.Id, null, "Flete Internacional",
                CostDetailType.Freight, CostType.Variable, ChargeBasis.PerChargeableCft,
                rate.CurrencyId, "USD", "USD", 2.80m, 87.50m, notes, 3.20m, null);
            using var report = Report(rate);
            var item = report.RootElement.GetProperty("items")[0];
            Assert.AreEqual(string.Empty, item.GetProperty("miamiBreakdown").GetString());
        }
    }

    private static JsonDocument Report(RateHeader rate) =>
        JsonDocument.Parse(new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));

    private static RateHeader CreateRate(string polName, string polCode) => RateHeader.Create(
        rateCode: "QUO-MIA-TEST", sourceImportFclRateId: null,
        agentId: Guid.NewGuid(), agentName: "Grupo Castro Fallas", agentCode: "GCF",
        carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
        polId: Guid.NewGuid(), polName: polName, polCode: polCode,
        poeId: Guid.NewGuid(), poeName: "Moín, Costa Rica", poeCode: "CRMOIN",
        podId: null, podName: null, podCode: null,
        containerTypeId: Guid.NewGuid(), containerTypeName: "LCL", containerTypeCode: "LCL",
        incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "FOB",
        currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
        freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(1),
        containerQuantity: 1, clientName: "Cliente Miami", idtraNumber: null, quoNumber: null,
        includes: null, subjectTo: null, excludes: null, transitTime: null,
        rateType: RateType.Spot, createdBy: null);
}
