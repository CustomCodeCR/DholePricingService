using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class OwnLclOriginScopeTests
{
    private static readonly IReadOnlyDictionary<string, decimal> ShanghaiMatrix =
        new Dictionary<string, decimal> { ["SHANGHAI"] = 0m, ["NINGBO"] = 52m, ["QINGDAO"] = 52m };

    [TestMethod]
    public void Shanghai_UsesTheFullMultiOriginMatrix()
    {
        var result = OwnLclOriginScope.ScenarioOrigins("POL-2026-001", "Shanghai, China", ShanghaiMatrix);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(52m, result["NINGBO"]);
        Assert.IsTrue(OwnLclOriginScope.UsesShanghaiMatrix("CNSHA", null));
    }

    [TestMethod]
    public void Yantian_UsesOnlyItsOwnPol_WithNoShanghaiSurcharge()
    {
        var result = OwnLclOriginScope.ScenarioOrigins("POL-2026-074", "Yantian, China", ShanghaiMatrix);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0m, result["YANTIAN"]);
        Assert.IsFalse(result.ContainsKey("SHANGHAI"));
        Assert.IsFalse(result.ContainsKey("NINGBO"));
        Assert.IsTrue(OwnLclOriginScope.MatchesSelectedPol("CNYTN", "POL-2026-074", "Yantian, China"));
        Assert.IsTrue(OwnLclOriginScope.MatchesSelectedPol("POL-2026-074", "POL-2026-074", "Yantian, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("SHANGHAI", "POL-2026-074", "Yantian, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("NINGBO", "POL-2026-074", "Yantian, China"));
    }

    [TestMethod]
    public void OtherPorts_AreScopedToTheirOwnOrigin()
    {
        var result = OwnLclOriginScope.ScenarioOrigins("CNNGB", "Ningbo, China", ShanghaiMatrix);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0m, result["NINGBO"]);
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("CNSHA", "CNNGB", "Ningbo, China"));
        Assert.AreEqual("YANTIAN", OwnLclOriginScope.Resolve("CNYTN", "Yantian, Shenzhen, China"));
    }

    [TestMethod]
    public void Guangzhou_CanUseYantianOwnConsolidation_ButNeverShanghai()
    {
        // Both commercial name and UN/LOCODE must resolve to the physical Yantian departure.
        Assert.IsTrue(OwnLclOriginScope.MatchesSelectedPol("GUANGZHOU", "POL-2026-102", "Yantian, China"));
        Assert.IsTrue(OwnLclOriginScope.MatchesSelectedPol("CNCAN", "CNYTN", "Yantian, Shenzhen, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("GUANGZHOU", "CNSHA", "Shanghai, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("CNCAN", "CNNGB", "Ningbo, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("SHANGHAI", "CNYTN", "Yantian, China"));
        Assert.IsFalse(OwnLclOriginScope.MatchesSelectedPol("YANTIAN", "CNCAN", "Guangzhou, China"));

        var origins = OwnLclOriginScope.ScenarioOrigins("CNYTN", "Yantian, China", ShanghaiMatrix);
        Assert.AreEqual(1, origins.Count);
        Assert.AreEqual(0m, origins["YANTIAN"]);
        Assert.IsFalse(origins.ContainsKey("SHANGHAI"));
    }

    [TestMethod]
    public void Yantian_UsesShenzhenDifferentialOnlyWhenTheConsolidationIsShanghai()
    {
        // Existing CNCA matrix: the Shenzhen row is 62 USD/CBM above Shanghai.
        var shanghaiSurcharges = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHANGHAI"] = 0m,
            ["SHENZHEN"] = 62m,
        };
        var matrixRateOrigin = OwnLclOriginScope.ShanghaiRateOrigin("YANTIAN");
        Assert.AreEqual("SHENZHEN", matrixRateOrigin);
        Assert.AreEqual("SHENZHEN", OwnLclOriginScope.ShanghaiRateOrigin("CNYTN"));
        Assert.AreEqual(62m, shanghaiSurcharges[matrixRateOrigin]);
        Assert.AreEqual("SHENZHEN", OwnLclOriginScope.ShanghaiRateOrigin("CNSZX"));
        Assert.AreEqual("GUANGZHOU", OwnLclOriginScope.ShanghaiRateOrigin("CNCAN"));

        // The own Yantian consolidation has no Shanghai pickup surcharge and
        // must never be treated as a Shanghai sailing.
        Assert.IsFalse(OwnLclOriginScope.UsesShanghaiMatrix("POL-2026-102", "Yantian, China"));
        var yantian = OwnLclOriginScope.ScenarioOrigins(
            "POL-2026-102", "Yantian, China", shanghaiSurcharges);
        Assert.AreEqual(1, yantian.Count);
        Assert.AreEqual(0m, yantian["YANTIAN"]);
        Assert.IsFalse(yantian.ContainsKey("SHENZHEN"));
    }
}
