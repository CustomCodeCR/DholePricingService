using System.Reflection;
using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Application.Abstractions.Services;
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
    [TestMethod]
    public void SelectedConfigPols_ReconcileEveryMgfMoinPublishedOriginWithoutCrossCarrierExpansion()
    {
        var names = new[]
        {
            "Yantian, China", "Xiamen, China", "Qingdao, China",
            "Xingang, China", "Hong Kong, China", "Shanghai, China",
            "Ningbo, China", "Shenzhen, China", "Busan, Korea"
        };
        var ports = names.Select((name, index) => new PricingConfigCatalogItem(
            Guid.NewGuid(), "pol", "POL-" + index, name.ToLowerInvariant().Replace(' ', '-'),
            name, name, null)).ToArray();

        // These 32 lanes are the actual MGF Moín 2026-10-15/21 matrix.
        var byCarrier = new Dictionary<string, string[]>
        {
            ["YANG MING"] = ["YANTIAN", "XIAMEN", "QINGDAO", "XINGANG", "HONG KONG"],
            ["ONE"] = ["SHANGHAI", "NINGBO", "SHENZHEN", "QINGDAO", "XIAMEN", "HONG KONG", "BUSAN"],
            ["MSC"] = ["SHANGHAI", "NINGBO", "SHENZHEN", "QINGDAO", "XIAMEN", "XINGANG", "HONG KONG", "BUSAN"],
            ["EVERGREEN"] = ["HONG KONG", "SHANGHAI", "NINGBO", "YANTIAN"],
            ["COSCO"] = ["SHANGHAI", "NINGBO", "SHENZHEN", "QINGDAO", "XIAMEN", "XINGANG", "HONG KONG", "BUSAN"],
        };
        Assert.AreEqual(32, byCarrier.Sum(group => group.Value.Length));

        var type = typeof(CompetitorTariffEndpoints).Assembly.GetType(
            "Dhole.Pricing.Api.Endpoints.CompetitorTariffRouteOverride");
        Assert.IsNotNull(type);
        var matcher = type.GetMethod("MatchSelectedPol",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(matcher);

        foreach (var (carrier, origins) in byCarrier)
        {
            foreach (var origin in origins)
            {
                var mappedId = (Guid?)matcher.Invoke(null,
                    new object?[] { origin, null, ports });
                Assert.IsTrue(mappedId.HasValue,
                    "Config has the origin " + origin + " for " + carrier);
                Assert.IsTrue(ports.Any(port => port.Id == mappedId.Value));
            }
        }

        // A user selection must not create a route absent from the original PDF.
        Assert.IsNull(matcher.Invoke(null, new object?[] { "DALIAN", null, ports }));
    }

    [TestMethod]
    public void SelectedConfigPols_HonorManualChoiceAndKeepAmbiguousRowsForReview()
    {
        var moin = new PricingConfigCatalogItem(Guid.NewGuid(), "pol",
            "POL-MOIN", "moin", "Puerto Moín, Costa Rica", null, null);
        var hongKong = new PricingConfigCatalogItem(Guid.NewGuid(), "pol",
            "POL-HKG", "hong-kong", "Hong Kong, China", null, null);
        var duplicate = hongKong with { Id = Guid.NewGuid(), Code = "POL-HKG-2" };
        var type = typeof(CompetitorTariffEndpoints).Assembly.GetType(
            "Dhole.Pricing.Api.Endpoints.CompetitorTariffRouteOverride");
        Assert.IsNotNull(type);
        var matcher = type.GetMethod("MatchSelectedPol",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(matcher);

        Assert.AreEqual(hongKong.Id,
            matcher.Invoke(null, new object?[] { "HONG KONG", null, new[] { hongKong } }));
        Assert.IsNull(matcher.Invoke(null,
            new object?[] { "HONG KONG", null, new[] { hongKong, duplicate } }));
        Assert.AreEqual(hongKong.Id, matcher.Invoke(null,
            new object?[] { "HONG KONG", (Guid?)hongKong.Id, new[] { hongKong, duplicate } }));
        Assert.IsNull(matcher.Invoke(null,
            new object?[] { "BUSAN", null, new[] { hongKong, moin } }));
    }

}
