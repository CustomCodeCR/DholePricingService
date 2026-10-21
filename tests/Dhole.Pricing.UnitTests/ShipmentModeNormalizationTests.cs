using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class ShipmentModeNormalizationTests
{
    [DataTestMethod]
    [DataRow("Air")]
    [DataRow("airconsol")]
    [DataRow(" AIRCONSOL ")]
    public void IsAir_AcceptsBothAliases(string modality)
    {
        Assert.IsTrue(ShipmentModeNormalization.IsAir(modality));
    }

    [DataTestMethod]
    [DataRow("Lcl")]
    [DataRow("Maritime")]
    public void IsAir_DoesNotClassifyMaritimeAsAir(string modality)
    {
        Assert.IsFalse(ShipmentModeNormalization.IsAir(modality));
    }

    [TestMethod]
    public void Normalize_And_Equality_AreBidirectional()
    {
        Assert.AreEqual(ShipmentMode.AirConsol, ShipmentModeNormalization.Normalize(ShipmentMode.Air));
        Assert.IsTrue(ShipmentModeNormalization.AreEquivalent(ShipmentMode.Air, ShipmentMode.AirConsol));
        Assert.IsTrue(ShipmentModeNormalization.AreEquivalent(ShipmentMode.AirConsol, ShipmentMode.Air));
        Assert.IsFalse(ShipmentModeNormalization.AreEquivalent(ShipmentMode.AirConsol, ShipmentMode.Lcl));
    }

    [TestMethod]
    public void ExpandAliases_MatchesLegacyAndCanonicalModesWithoutOtherModalities()
    {
        var aliases = ShipmentModeNormalization.ExpandAliases([ShipmentMode.AirConsol]);
        CollectionAssert.AreEquivalent(new[] { ShipmentMode.Air, ShipmentMode.AirConsol }, aliases.ToArray());
        var legacy = ShipmentModeNormalization.ExpandAliases([ShipmentMode.Air]);
        CollectionAssert.AreEquivalent(new[] { ShipmentMode.Air, ShipmentMode.AirConsol }, legacy.ToArray());
        CollectionAssert.AreEquivalent(new[] { ShipmentMode.Lcl },
            ShipmentModeNormalization.ExpandAliases([ShipmentMode.Lcl]).ToArray());
    }

    [TestMethod]
    public void CostMasks_CanonicalizeNewModes_ButMatchBothLegacyAndCanonicalQueries()
    {
        var mask = Cost.BuildShipmentModeMask([ShipmentMode.Air]);
        var canonicalMask = Cost.BuildShipmentModeMask([ShipmentMode.AirConsol]);
        Assert.AreEqual(canonicalMask, mask);
        Assert.IsFalse((mask & Cost.BuildShipmentModeMask([ShipmentMode.Lcl])) != 0);
    }
}
