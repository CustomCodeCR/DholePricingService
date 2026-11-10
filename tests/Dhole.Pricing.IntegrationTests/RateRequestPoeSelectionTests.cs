using System.Text.Json;
using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestPoeSelectionTests
{
    private static readonly Guid Caldera = Guid.Parse("b0b25b94-17c7-488d-ac4b-8d8fa077c218");
    private static readonly Guid Moin = Guid.Parse("71dcc3ad-ab06-4a59-a4ec-061cc037f4c7");

    [TestMethod]
    public void MaritimeFcl_StoresAndReturnsAllDistinctPoes()
    {
        using var document = JsonDocument.Parse($$"""
            {"selectedPoeIds":["{{Caldera}}","{{Moin}}"],"selectedPoeNames":["Puerto Caldera","Puerto Moín"]}
            """);
        Assert.IsNull(RateRequestPoeSelection.Validate(document.RootElement, "Fcl", Caldera));
        var ids = RateRequestPoeSelection.Ids(document.RootElement, Caldera);
        var names = RateRequestPoeSelection.Names(document.RootElement, "Puerto Caldera");
        Assert.AreEqual(2, ids.Length);
        Assert.AreEqual(Caldera.ToString("D"), ids[0]);
        Assert.AreEqual(Moin.ToString("D"), ids[1]);
        Assert.AreEqual(2, names.Length);
        Assert.AreEqual("Puerto Moín", names[1]);
    }

    [TestMethod]
    public void RejectsDuplicatePoe_AndMultiplePoeOutsideFcl()
    {
        using var duplicate = JsonDocument.Parse($$"""
            {"selectedPoeIds":["{{Caldera}}","{{Caldera}}"]}
            """);
        Assert.IsNotNull(RateRequestPoeSelection.Validate(duplicate.RootElement, "Fcl", Caldera));

        using var nonFcl = JsonDocument.Parse($$"""
            {"selectedPoeIds":["{{Caldera}}","{{Moin}}"]}
            """);
        Assert.IsNotNull(RateRequestPoeSelection.Validate(nonFcl.RootElement, "Lcl", Caldera));
        Assert.IsNotNull(RateRequestPoeSelection.Validate(nonFcl.RootElement, "Fcl", Guid.NewGuid()));
    }

    [TestMethod]
    public void LegacyPayloadKeepsPrimaryPoe()
    {
        using var legacy = JsonDocument.Parse("{}");
        Assert.IsNull(RateRequestPoeSelection.Validate(legacy.RootElement, "Lcl", Caldera));
        Assert.AreEqual(Caldera.ToString("D"), RateRequestPoeSelection.Ids(legacy.RootElement, Caldera)[0]);
        Assert.AreEqual("Puerto Caldera", RateRequestPoeSelection.Names(legacy.RootElement, "Puerto Caldera")[0]);
    }
}
