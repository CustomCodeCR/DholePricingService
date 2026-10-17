using System.Reflection;
using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Contracts.Competitors;

namespace Dhole.Pricing.CompetitorPdfTests;

[TestClass]
public sealed class CompetitorMultiPoeRoutesTests
{
    [TestMethod]
    public void NewContract_AcceptsMultiplePoeIds()
    {
        var pol = Guid.NewGuid();
        var moin = Guid.NewGuid();
        var caldera = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new { polIds = new[] { pol }, poeIds = new[] { moin, caldera } });
        var request = JsonSerializer.Deserialize<SetCompetitorTariffRoutesRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(request);
        Assert.HasCount(2, request.PoeIds!);
        Assert.Contains(moin, request.PoeIds);
        Assert.Contains(caldera, request.PoeIds);
    }

    [TestMethod]
    public void LegacyContract_RemainsValidForSinglePoe()
    {
        var port = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new { polIds = new[] { Guid.NewGuid() }, poeId = port });
        var request = JsonSerializer.Deserialize<SetCompetitorTariffRoutesRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(request);
        Assert.AreEqual(port, request.PoeId);
        Assert.IsNull(request.PoeIds);
    }

    [TestMethod]
    public void OriginalPdfPol_IsRecoveredFromNestedSourceRow_WithoutInventingCoverage()
    {
        var row = JsonSerializer.Serialize(new { OriginPort = "XINGANG", PortOfExit = "Moin" });
        var payload = JsonSerializer.Serialize(new { RawJson = row, RequiresManualReview = false });
        var routeType = typeof(CompetitorTariffEndpoints).Assembly.GetType(
            "Dhole.Pricing.Api.Endpoints.CompetitorTariffRouteOverride");
        Assert.IsNotNull(routeType);
        var reader = routeType.GetMethod("ReadOriginalPort",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(reader);
        Assert.AreEqual("XINGANG", reader.Invoke(null, new object?[] { payload, "OriginPort" }));
        Assert.AreEqual("Moin", reader.Invoke(null, new object?[] { payload, "PortOfExit" }));
    }
}
