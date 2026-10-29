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
}
