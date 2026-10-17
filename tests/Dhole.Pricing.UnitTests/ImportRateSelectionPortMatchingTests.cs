using System.Reflection;
using Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ImportRateSelectionPortMatchingTests
{
    private static bool PortMatches(string requested, string name, string code, string slug = "")
    {
        var method = typeof(GetImportRatesForSelectQueryHandler).GetMethod(
            "PortMatches",
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException("Port matching helper missing");

        return (bool)(method.Invoke(null, [requested, name, code, slug])
            ?? throw new InvalidOperationException("Port matcher returned null"));
    }

    [TestMethod]
    public void ShanghaiMatchesItsUnLocode()
    {
        Assert.IsTrue(PortMatches("Shanghai, China", "CNSHA", "CNSHA"));
        Assert.IsTrue(PortMatches("CNSHA", "Shanghai, China", "POL-2026-123"));
    }

    [TestMethod]
    public void CalderaMatchesItsUnLocode()
    {
        Assert.IsTrue(PortMatches("Puerto Caldera, Costa Rica", "Caldera", "CRCAL"));
        Assert.IsTrue(PortMatches("CRCAL", "Puerto Caldera, Costa Rica", "POE-2026-001"));
    }

    [TestMethod]
    public void DoesNotConfuseOtherOriginsOrDestinations()
    {
        Assert.IsFalse(PortMatches("Shanghai, China", "Ningbo, China", "CNNGB"));
        Assert.IsFalse(PortMatches("Puerto Caldera, Costa Rica", "Puerto Moin, Costa Rica", "CRMOI"));
    }
}
