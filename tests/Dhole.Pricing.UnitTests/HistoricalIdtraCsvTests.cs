using Dhole.Pricing.Application.Features.Rates.SetRateStatus;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class HistoricalIdtraCsvTests
{
    [TestMethod]
    public void SplitsAndDeduplicatesExplicitIdentifiersWithoutChangingSpelling()
    {
        Assert.IsTrue(AcceptedQuoteSnapshot.TryReadHistoricalIdtras("IDTRA999.12589, IDTRA12964, IDTRA999.12589", out var values));
        CollectionAssert.AreEqual(new[] { "IDTRA999.12589", "IDTRA12964" }, values);
    }

    [TestMethod]
    public void RejectsAmbiguousOrNonNumericIdentifiers()
    {
        Assert.IsFalse(AcceptedQuoteSnapshot.TryReadHistoricalIdtras("IDTRA123, IDTRA999.123", out _));
        Assert.IsFalse(AcceptedQuoteSnapshot.TryReadHistoricalIdtras("IDTRA123, invalid", out _));
    }
}
