using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportAirLclParityTests
{
    private const decimal PoundsPerKg = 2.20462262m;

    [TestMethod]
    public void AirLcl_PerPoundFreight_UsesRealWeightAndMatchesCommercialLines()
    {
        // Regression for QUO-00000-000448: a USD 5.10/lb rate was
        // incorrectly persisted/rendered as 1 x 5.10 instead of its cargo weight.
        var rate = CreateAirRate();
        var freight = rate.AddRateDetail(rate.Id, null, "Flete Internacional",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerLb,
            rate.CurrencyId, "USD", "USD", 4.63m, 5.10m, null, 1m, null);

        Assert.AreEqual(
            decimal.Round(54.885m * PoundsPerKg, 6),
            decimal.Round(freight.Quantity, 6),
            "PerLb detail persistence must use actual billable pounds.");

        var extras = new (string Name, decimal Sale)[]
        {
            ("AFT", 90m),
            ("AWB", 0m),
            ("Cargos en Origen", 0m),
            ("FUEL", 25m),
            ("PEAK SEASSON SURCHARGE", 25m),
            ("PS", 55m),
            ("SCREENING", 25m),
            ("SED", 25m),
            ("AWB", 50m),
            ("Manejos", 55m),
            ("Retiro de AWB", 30m),
        };
        foreach (var (name, sale) in extras)
        {
            rate.AddRateDetail(rate.Id, null, name, CostDetailType.Other,
                CostType.Variable, ChargeBasis.PerShipment,
                rate.CurrencyId, "USD", "USD", 0m, sale, null, 1m, null);
        }

        using var report = JsonDocument.Parse(
            new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));

        var root = report.RootElement;
        var rows = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(12, rows.Length, "PDF must include zero-sale persisted rows as in Pricing.");
        var freightRow = rows.Single(x => x.GetProperty("description").GetString() == "Flete Internacional");
        Assert.AreEqual(54.885m, freightRow.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(
            decimal.Round(5.10m * PoundsPerKg, 2),
            decimal.Round(freightRow.GetProperty("unitSaleAmount").GetDecimal(), 2));
        Assert.AreEqual(
            617.10m,
            decimal.Round(freightRow.GetProperty("lineTotalAmount").GetDecimal(), 2));
        Assert.AreEqual(2,
            rows.Count(x => x.GetProperty("lineTotalAmount").GetDecimal() == 0m));
        Assert.AreEqual(997.10m,
            decimal.Round(root.GetProperty("currencyTotals")[0].GetProperty("amount").GetDecimal(), 2));
        StringAssert.Contains(root.GetProperty("rate").GetProperty("containerType").GetString()!,
            "54.885 KG");
        // The rows exported to Excel must use the very same commercial projection.
        var exportedFreight = root.GetProperty("rows").EnumerateArray()
            .Single(x => x.GetProperty("Concepto").GetString() == "Flete Internacional");
        Assert.AreEqual(54.885m, exportedFreight.GetProperty("Cantidad").GetDecimal());
        Assert.AreEqual(617.10m,
            decimal.Round(exportedFreight.GetProperty("Total").GetDecimal(), 2));
    }

    [TestMethod]
    public void AirLcl_PerKgFreight_IsNotConvertedAgain()
    {
        var rate = CreateAirRate();
        rate.AddRateDetail(rate.Id, null, "Flete Internacional",
            CostDetailType.Freight, CostType.Variable, ChargeBasis.PerKg,
            rate.CurrencyId, "USD", "USD", 10.21m, 11.24m, null, 1m, null);

        using var report = JsonDocument.Parse(
            new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));
        var freight = report.RootElement.GetProperty("items")[0];
        Assert.AreEqual(54.885m, freight.GetProperty("quantity").GetDecimal());
        Assert.AreEqual(11.24m, freight.GetProperty("unitSaleAmount").GetDecimal());
        Assert.AreEqual(616.9074m, freight.GetProperty("lineTotalAmount").GetDecimal());
    }

    private static RateHeader CreateAirRate()
    {
        var rate = RateHeader.Create(
            rateCode: "QUO-AIR-TEST", sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Grupo Castro Fallas", agentCode: "GCF",
            carrierId: Guid.NewGuid(), carrierName: "Air carrier", carrierCode: "AIR",
            polId: Guid.NewGuid(), polName: "Miami, Estados Unidos (MIA)", polCode: "MIA",
            poeId: Guid.NewGuid(), poeName: "San José, Costa Rica (SJO)", poeCode: "SJO",
            podId: null, podName: null, podCode: null,
            containerTypeId: Guid.NewGuid(), containerTypeName: "LCL", containerTypeCode: "LCL",
            incotermId: Guid.NewGuid(), incotermName: "FCA", incotermCode: "FCA",
            currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
            freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(7),
            containerQuantity: 1, clientName: "Cliente aéreo", idtraNumber: null, quoNumber: null,
            includes: null, subjectTo: null, excludes: null, transitTime: "2 días",
            rateType: RateType.Spot, createdBy: null);
        rate.ConfigureShipment(ShipmentMode.Lcl, totalPackages: 1, totalPallets: 1,
            totalWeightKg: 54.885m, totalVolumeCbm: 0.248m,
            kgPerCbm: 500m, cargoLinesJson: null, updatedBy: null);
        return rate;
    }
}
