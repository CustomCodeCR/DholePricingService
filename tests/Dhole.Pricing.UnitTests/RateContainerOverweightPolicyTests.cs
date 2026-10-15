using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateContainerOverweightPolicyTests
{
    [TestMethod]
    public void CostaRica_OnlyUnitsStrictlyAboveLimit()
        => Assert.AreEqual(2, RateContainerOverweightPolicy.CountOverweight(
            "Puerto Caldera, Costa Rica", [21500m, 21501m, 24000m, 18000m]));
    [TestMethod]
    public void PanamaIsExcluded()
        => Assert.AreEqual(0, RateContainerOverweightPolicy.CountOverweight("Balboa, Panamá", [30000m]));
    [TestMethod]
    public void AppliesToOtherCentralAmericanCountries()
    {
        foreach (var name in new[] { "Nicaragua", "Honduras", "Guatemala", "El Salvador", "Belice" })
            Assert.AreEqual(1, RateContainerOverweightPolicy.CountOverweight(name, [22000m]), name);
    }
}
