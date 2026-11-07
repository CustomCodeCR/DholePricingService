using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class QuoteCurrencyValueTests
{
    [TestMethod]
    public void GroupedQuote_OceanFreight_UsesCatalogValue_InsteadOfInternalCode()
    {
        Assert.AreEqual("USD", QuoteCurrencyValue.Resolve("USD", "CUR-2026-001"));
        Assert.AreEqual("CRC", QuoteCurrencyValue.Resolve("CRC", "USD"));
        Assert.AreEqual("USD", QuoteCurrencyValue.Resolve(null, "USD"));
    }

    [TestMethod]
    public void UnresolvedValue_NeverRevealsCatalogCode_OrAssumesUsd()
    {
        Assert.AreEqual("—", QuoteCurrencyValue.Resolve("CUR-2026-001", null));
        Assert.AreEqual("—", QuoteCurrencyValue.Resolve(null, "CUR-2026-001"));
        Assert.AreEqual("—", QuoteCurrencyValue.Resolve("  ", ""));
    }
}
