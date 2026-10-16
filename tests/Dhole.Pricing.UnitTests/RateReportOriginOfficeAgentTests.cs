using System.Text.Json;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateReportOriginOfficeAgentTests
{
    [TestMethod]
    public void OwnMaritimeLcl_UsesRsInPdfAgentAndPublicOriginLink()
    {
        var rate = CreateRate("GCF", "Grupo Castro Fallas");
        rate.ConfigureShipment(ShipmentMode.Lcl, 1, 0, 100m, 1m, 500m, null, null);
        AddFreight(rate, ChargeBasis.PerCbm, "LCL PROPIO · ConsolidadoId: 44444444-4444-4444-4444-444444444444");

        using var report = Report(rate);
        AssertAgent(report, "RS", "RS");
    }

    [TestMethod]
    public void LegacyOwnMaritimeLcl_WithGcfHeader_UsesRs()
    {
        var rate = CreateRate("GCF", "Grupo Castro Fallas");
        rate.ConfigureShipment(ShipmentMode.Lcl, 1, 0, 100m, 1m, 500m, null, null);

        using var report = Report(rate);
        AssertAgent(report, "RS", "RS");
    }

    [TestMethod]
    public void GcfColoaderLcl_IsNotReclassifiedAsOwn()
    {
        var rate = CreateRate("GCF", "Grupo Castro Fallas");
        rate.ConfigureShipment(ShipmentMode.Lcl, 1, 0, 100m, 1m, 500m, null, null);
        AddFreight(rate, ChargeBasis.PerCbm, "LCL COLOADER · RateId: 44444444-4444-4444-4444-444444444444");

        using var report = Report(rate);
        AssertAgent(report, "Grupo Castro Fallas", "GCF");
    }

    [TestMethod]
    public void AirLcl_WithOwnMarker_DoesNotUseRs()
    {
        var rate = CreateRate("GCF", "Grupo Castro Fallas");
        rate.ConfigureShipment(ShipmentMode.Lcl, 1, 0, 100m, 1m, 500m, null, null);
        AddFreight(rate, ChargeBasis.PerKg, "LCL PROPIO · ConsolidadoId: 44444444-4444-4444-4444-444444444444");

        using var report = Report(rate);
        AssertAgent(report, "Grupo Castro Fallas", "GCF");
    }

    [TestMethod]
    public void MaritimeFcl_DoesNotChangeAgent()
    {
        using var report = Report(CreateRate("GCF", "Grupo Castro Fallas"));
        AssertAgent(report, "Grupo Castro Fallas", "GCF");
    }

    private static JsonDocument Report(RateHeader rate) =>
        JsonDocument.Parse(new RateReportDataFactory(new ConfigurationBuilder().Build()).CreateDataJson(rate));

    private static void AssertAgent(JsonDocument report, string expectedName, string expectedCode)
    {
        Assert.AreEqual(expectedName,
            report.RootElement.GetProperty("rate").GetProperty("agent").GetString());
        var publicUrl = report.RootElement.GetProperty("originOffice").GetProperty("publicPageUrl").GetString();
        Assert.IsNotNull(publicUrl);
        StringAssert.Contains(publicUrl, $"&agentCode={expectedCode}");
        StringAssert.Contains(publicUrl, $"&agent={Uri.EscapeDataString(expectedName)}");
    }

    private static void AddFreight(RateHeader rate, ChargeBasis basis, string notes) =>
        rate.AddRateDetail(rate.Id, null, "Flete internacional",
            CostDetailType.Freight, CostType.Variable, basis,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode,
            10m, 15m, notes, 1m, null);

    private static RateHeader CreateRate(string agentCode, string agentName) => RateHeader.Create(
        rateCode: "QUO-RS-LCL-TEST", sourceImportFclRateId: null,
        agentId: Guid.NewGuid(), agentName: agentName, agentCode: agentCode,
        carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
        polId: Guid.NewGuid(), polName: "Qingdao, China", polCode: "CNTAO",
        poeId: Guid.NewGuid(), poeName: "Caldera, Costa Rica", poeCode: "CRCAL",
        podId: null, podName: null, podCode: null,
        containerTypeId: Guid.NewGuid(), containerTypeName: "LCL", containerTypeCode: "LCL",
        incotermId: Guid.NewGuid(), incotermName: "FOB", incotermCode: "FOB",
        currencyId: Guid.NewGuid(), currencyName: "USD", currencyCode: "USD",
        freeDays: 0, validFrom: DateTime.UtcNow.Date, validTo: DateTime.UtcNow.Date.AddDays(1),
        containerQuantity: 1, clientName: "Cliente", idtraNumber: null, quoNumber: null,
        includes: null, subjectTo: null, excludes: null, transitTime: null,
        rateType: RateType.Spot, createdBy: null);
}
