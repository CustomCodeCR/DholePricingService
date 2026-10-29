using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class CastroFallasRateEmailRecipientPolicyTests
{
    [DataTestMethod]
    [DataRow("Castro Fallas", "jmesen@castrofallas.com", false)]
    [DataRow("  CASTRO   FALLAS  ", " JMESEN@CASTROFALLAS.COM ", false)]
    [DataRow("Castro Fallas", "pricingcf@castrofallas.com", true)]
    [DataRow("Castro Fallas", "cquiros@castrofallas.com", true)]
    [DataRow("Otro vendedor", "jmesen@castrofallas.com", true)]
    [DataRow(null, "jmesen@castrofallas.com", true)]
    public void Suppresses_only_jmesen_for_corporate_seller(
        string? executiveName, string recipientEmail, bool expected)
    {
        Assert.AreEqual(expected,
            CastroFallasRateEmailRecipientPolicy.CanReceive(executiveName, recipientEmail));
    }
}
