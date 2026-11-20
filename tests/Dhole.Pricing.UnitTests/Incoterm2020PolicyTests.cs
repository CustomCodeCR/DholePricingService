using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class Incoterm2020PolicyTests
{
    [TestMethod]
    [DataRow("EXW", false, false)]
    [DataRow("FCA", false, false)]
    [DataRow("FAS", false, true)]
    [DataRow("FOB", false, true)]
    [DataRow("CFR", true, true)]
    [DataRow("CIF", true, true)]
    [DataRow("CPT", true, false)]
    [DataRow("CIP", true, false)]
    [DataRow("DAP", true, false)]
    [DataRow("DPU", true, false)]
    [DataRow("DDP", true, false)]
    public void AllIccTerms_AreKnownAndKeepTheirMainCarriageAndMode(
        string code, bool sellerPaysFreight, bool maritimeOnly)
    {
        var rule = Incoterm2020Policy.Describe("INC-2026-011", $"Incoterm {code}");
        Assert.IsNotNull(rule);
        Assert.AreEqual(code, rule.Code);
        Assert.AreEqual(sellerPaysFreight, rule.SellerPaysMainCarriage);
        Assert.AreEqual(maritimeOnly, rule.SeaOrInlandWaterwayOnly);
    }

    [TestMethod]
    public void FreightAndRiskRemainSeparateForCifAndCip()
    {
        Assert.AreEqual("A bordo del buque en origen", Incoterm2020Policy.Describe("CIF")!.RiskTransfers);
        Assert.AreEqual("Entrega al primer transportista", Incoterm2020Policy.Describe("CIP")!.RiskTransfers);
        Assert.IsTrue(Incoterm2020Policy.Describe("CIF")!.SellerMustInsure);
        Assert.IsTrue(Incoterm2020Policy.Describe("CIP")!.SellerMustInsure);
    }

    [TestMethod]
    public void ImportCustomsAndUnloadingFollowDdpAndDpu()
    {
        Assert.AreEqual("Exporter", Incoterm2020Policy.RecommendPayer("DDP", "importcustoms"));
        Assert.AreEqual("Importer", Incoterm2020Policy.RecommendPayer("DAP", "importtaxes"));
        Assert.AreEqual("Exporter", Incoterm2020Policy.RecommendPayer("DPU", "unloading"));
        Assert.AreEqual("Importer", Incoterm2020Policy.RecommendPayer("DDP", "unloading"));
        Assert.AreEqual("ByAgreement", Incoterm2020Policy.RecommendPayer("CIF", "terminal"));
    }

    [TestMethod]
    public void ExceptionalPayerRequiresReasonButNeverProhibitsService()
    {
        Assert.IsTrue(Incoterm2020Policy.RequiresOverrideReason("FOB", "mainfreight", "Exporter"));
        Assert.IsFalse(Incoterm2020Policy.RequiresOverrideReason("FOB", "mainfreight", "Importer"));
        Assert.IsFalse(Incoterm2020Policy.RequiresOverrideReason("CIF", "terminal", "Exporter"));
        Assert.IsFalse(Incoterm2020Policy.IsTransportModeRecommended("FOB", false));
        Assert.IsTrue(Incoterm2020Policy.IsTransportModeRecommended("CPT", false));
    }
}
