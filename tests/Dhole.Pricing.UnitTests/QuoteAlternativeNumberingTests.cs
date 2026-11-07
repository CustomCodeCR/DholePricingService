using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class QuoteAlternativeNumberingTests
{
    private const string Parent = "QUO-00000-000600";
    private static readonly Guid Caldera = Guid.Parse("12345678-0000-0000-0000-000000000001");
    private static readonly Guid Moin = Guid.Parse("12345678-0000-0000-0000-000000000002");
    private static readonly Guid Msc = Guid.Parse("12345678-0000-0000-0000-000000000003");
    private static readonly Guid Maersk = Guid.Parse("12345678-0000-0000-0000-000000000004");

    [DataTestMethod]
    [DataRow(0, "A")]
    [DataRow(25, "Z")]
    [DataRow(26, "AA")]
    [DataRow(27, "AB")]
    [DataRow(701, "ZZ")]
    [DataRow(702, "AAA")]
    public void Suffix_IsAlphabeticAndStable(int index, string suffix)
    {
        Assert.AreEqual(suffix, QuoteAlternativeNumbering.SuffixAt(index));
        Assert.AreEqual(index, QuoteAlternativeNumbering.IndexOfSuffix(suffix));
        Assert.AreEqual(Parent + "-" + suffix, QuoteAlternativeNumbering.SubQuoteNumber(Parent, suffix));
    }

    [TestMethod]
    public void TwoPoesAndTwoCarriers_CreateAThroughDWithoutCrossingRoutes()
    {
        var alternatives = new[]
        {
            Candidate(Caldera, Msc),
            Candidate(Caldera, Maersk),
            Candidate(Moin, Msc),
            Candidate(Moin, Maersk)
        };
        var options = QuoteAlternativeNumbering.Append(Parent, alternatives, []);
        CollectionAssert.AreEqual(new[] { Parent + "-A", Parent + "-B", Parent + "-C", Parent + "-D" },
            options.Select(option => option.QuoteNumber).ToArray());

        var reversed = QuoteAlternativeNumbering.Append(Parent, alternatives.Reverse().ToArray(), options);
        CollectionAssert.AreEqual(options.Select(option => option.QuoteNumber).ToArray(),
            reversed.Select(option => option.QuoteNumber).ToArray());

        var added = QuoteAlternativeNumbering.Append(Parent,
            [Candidate(Caldera, Msc, QuoteAlternativeHaulage.Carrier)], options);
        Assert.AreEqual(Parent + "-E", added.Last().QuoteNumber);
    }

    [TestMethod]
    public void AcceptanceProjection_IsExclusiveAndIdempotent()
    {
        var options = QuoteAlternativeNumbering.Append(Parent,
            [Candidate(Caldera, Msc), Candidate(Moin, Maersk)], []);
        var accepted = QuoteAlternativeNumbering.Select(options, options[1].Key);
        Assert.IsFalse(accepted[0].Accepted);
        Assert.IsTrue(accepted[1].Accepted);
        Assert.IsTrue(QuoteAlternativeNumbering.Select(accepted, options[1].Key)[1].Accepted);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            QuoteAlternativeNumbering.Select(accepted, options[0].Key));
    }

    [TestMethod]
    public void InvalidAndDuplicateOptions_CannotCauseAmbiguousCommercialNumbers()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            QuoteAlternativeNumbering.SubQuoteNumber("QUO-invalid", "A"));
        Assert.ThrowsExactly<ArgumentException>(() => QuoteAlternativeNumbering.IndexOfSuffix("a"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            QuoteAlternativeNumbering.Key(Candidate(Guid.Empty, Msc)));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            QuoteAlternativeNumbering.Append(Parent, [Candidate(Caldera, Msc)], []));
    }

    private static QuoteAlternativeCandidate Candidate(
        Guid poe, Guid carrier, QuoteAlternativeHaulage haulage = QuoteAlternativeHaulage.Merchant) =>
        new(poe, carrier, haulage);
}
