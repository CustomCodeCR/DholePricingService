using System.Text.Json;
using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuoteCurrencyTests
{
    [DataTestMethod]
    [DataRow("USD", "US DOLLAR", "USD")]
    [DataRow("CUR-2026-001", "US DOLLAR", "USD")]
    [DataRow("CUR-2026-001", "US Dollar (USD)", "USD")]
    [DataRow("CUR-2026-001", "Dólar estadounidense", "USD")]
    [DataRow("b3a963da-2eda-e611-80f2-fc15b42826a0", null, "USD")]
    [DataRow("CRC", "COLÓN COSTARRICENSE", "CRC")]
    [DataRow("CUR-2026-002", "Costa Rican Colon", "CRC")]
    public void ResolvesCatalogCurrenciesWithoutGuessingCurrencyIdentity(
        string code, string? name, string expected)
    {
        Assert.AreEqual(expected, DynamicsQuoteCurrency.ResolveIso(code, name));
    }

    [TestMethod]
    public void RejectsContradictoryOrUnknownCurrency()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuoteCurrency.ResolveIso("CRC", "US DOLLAR"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuoteCurrency.ResolveIso("CUR-unknown", "Unknown currency"));
    }

    [TestMethod]
    public void NormalizesOnlyOutboundPayloadAndKeepsOriginalSnapshot()
    {
        const string original = """
            {"currencyCode":"CUR-2026-001","currencyName":"US DOLLAR",
             "status":"AcceptedByClient","totalSaleAmount":10795.00,
             "idtraNumber":"IDTRA999.12661"}
            """;
        var iso = DynamicsQuoteCurrency.ResolveIso(original);
        var outbound = DynamicsQuoteCurrency.NormalizeSnapshot(original, iso);
        using var normalized = JsonDocument.Parse(outbound);
        using var persisted = JsonDocument.Parse(original);
        Assert.AreEqual("USD", normalized.RootElement.GetProperty("currencyCode").GetString());
        Assert.AreEqual("CUR-2026-001", persisted.RootElement.GetProperty("currencyCode").GetString());
        Assert.AreEqual(10795.00m, normalized.RootElement.GetProperty("totalSaleAmount").GetDecimal());
    }

    [TestMethod]
    public void ChecksOperatorSuppliedUsdGuidAgainstActualDataverseResponse()
    {
        var usd = Guid.Parse("b3a963da-2eda-e611-80f2-fc15b42826a0");
        DynamicsQuoteCurrency.VerifyResolvedIdentity(usd.ToString(), "USD", usd);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuoteCurrency.VerifyResolvedIdentity(usd.ToString(), "USD", Guid.NewGuid()));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicsQuoteCurrency.VerifyResolvedIdentity(usd.ToString(), "CRC", usd));
    }
}
