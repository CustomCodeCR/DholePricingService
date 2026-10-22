using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateQuotedFreightPolicyTests
{
    [TestMethod]
    [DataRow("CFR", "Cost and Freight")]
    [DataRow("CIF", "Cost, Insurance and Freight")]
    [DataRow("CPT", "Carriage Paid To")]
    [DataRow("CIP", "Carriage and Insurance Paid To")]
    [DataRow("DAP", "Delivered at Place")]
    [DataRow("DPU", "Delivered at Place Unloaded")]
    [DataRow("DDP", "Delivered Duty Paid")]
    public void NoFreightIncoterm_DoesNotRequireFreightDistribution(string code, string name)
    {
        Assert.IsTrue(RateQuotedFreightPolicy.ExcludesQuotedFreight(code, name));
    }

    [TestMethod]
    [DataRow("EXW")]
    [DataRow("FCA")]
    [DataRow("FAS")]
    [DataRow("FOB")]
    public void BuyerFreightIncoterm_RetainsDistributionValidation(string code)
    {
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(code, code));
    }

    [TestMethod]
    public void OpaqueConfigCode_UsesCifDisplayName()
    {
        Assert.IsTrue(RateQuotedFreightPolicy.ExcludesQuotedFreight(
            "incoterm-63f7ad", "CIF - Puerto Caldera"));
    }

    [TestMethod]
    public void UnrelatedNames_DoNotMatchIncotermSubstrings()
    {
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(
            "OTHER", "CIFRA Freight"));
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(null, null));
    }
}
