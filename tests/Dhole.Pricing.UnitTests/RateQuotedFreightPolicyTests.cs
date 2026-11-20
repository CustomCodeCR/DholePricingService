using Dhole.Pricing.Domain.Rates;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateQuotedFreightPolicyTests
{
    [TestMethod]
    [DataRow("EXW")]
    [DataRow("FCA")]
    [DataRow("FAS")]
    [DataRow("FOB")]
    [DataRow("CFR")]
    [DataRow("CIF")]
    [DataRow("CPT")]
    [DataRow("CIP")]
    [DataRow("DAP")]
    [DataRow("DPU")]
    [DataRow("DDP")]
    public void TemporaryUnblock_AllIncotermsAllowExplicitFreight(string term)
    {
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(term, term));
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight("INC-2026-005", term));
        foreach (var mode in new[] { ShipmentMode.Fcl, ShipmentMode.Lcl, ShipmentMode.Ltl,
                     ShipmentMode.Ftl, ShipmentMode.Air, ShipmentMode.AirConsol })
            Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(mode, "INC-2026-005", term));
        foreach (var operation in new[] { RateOperationType.Import, RateOperationType.Export,
                     RateOperationType.TransitDomestic })
            Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(operation, "INC-2026-005", term));
    }

    [TestMethod]
    public void TemporaryUnblock_NullAndUnknownIncotermsNeverSuppressFreight()
    {
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(null, null));
        Assert.IsFalse(RateQuotedFreightPolicy.ExcludesQuotedFreight(
            RateOperationType.Import, "OTHER", "OTHER"));
    }
}
