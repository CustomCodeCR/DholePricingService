using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class CommercialQuoteGroupProjectionTests
{
    [TestMethod]
    public void ChoosingOneAlternativeDoesNotRejectAnotherCommercialQuotation()
    {
        const string parent = "QUO-00000-000672";
        var first = new QuoteAlternativeCandidate(
            Guid.Parse("11111111-0000-0000-0000-000000000001"),
            Guid.Parse("11111111-0000-0000-0000-000000000002"),
            QuoteAlternativeHaulage.Merchant);
        var second = new QuoteAlternativeCandidate(
            Guid.Parse("11111111-0000-0000-0000-000000000003"),
            Guid.Parse("11111111-0000-0000-0000-000000000004"),
            QuoteAlternativeHaulage.Carrier);
        var options = QuoteAlternativeNumbering.Append(parent, [first, second], []);
        var chosen = QuoteAlternativeNumbering.Select(options, options[1].Key);
        Assert.AreEqual(2, chosen.Count);
        Assert.AreEqual(1, chosen.Count(item => item.Accepted));
        Assert.AreEqual(parent + "-A", chosen[0].QuoteNumber);
        Assert.AreEqual(parent + "-B", chosen[1].QuoteNumber);
        Assert.IsFalse(chosen[0].Accepted);
        Assert.IsTrue(chosen[1].Accepted);
        // Both sub-QUOs belong to the same public parent, not a rejected second QUO.
        Assert.IsTrue(chosen.All(item => item.QuoteNumber.StartsWith(parent + "-", StringComparison.Ordinal)));
    }
}
