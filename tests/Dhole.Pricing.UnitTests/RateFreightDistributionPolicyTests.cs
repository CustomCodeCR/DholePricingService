using Dhole.Pricing.Domain.Rates;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateFreightDistributionPolicyTests
{
    [TestMethod]
    [DataRow(ShipmentMode.Ltl)]
    [DataRow(ShipmentMode.Lcl)]
    [DataRow(ShipmentMode.Ftl)]
    [DataRow(ShipmentMode.Air)]
    [DataRow(ShipmentMode.AirConsol)]
    public void ConsolidatedAndLandModes_AcceptExplicitFreightWithoutFclAllocations(ShipmentMode mode)
    {
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            mode, Array.Empty<int>(), new decimal?[] { 1m }));
    }

    [TestMethod]
    public void CptLtl_OneCbmAndAnAdditionalTruckCharge_DoNotNeedContainers()
    {
        // Regression from the quoted Panama -> Nicaragua LTL payload:
        // Freight PerChargeableCbm (1) plus a separate TI / PerContainer charge.
        Assert.IsTrue(RateQuotedFreightPolicy.ExcludesQuotedFreight(
            ShipmentMode.Ltl, "INC-2026-007", "CPT"));
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Ltl, Array.Empty<int>(), new decimal?[] { 1m }));
    }

    [TestMethod]
    public void FclWithoutFreight_AllowsOnlyDestinationOrCustomsCharges()
    {
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 2 }, Array.Empty<decimal?>()));
    }

    [TestMethod]
    public void FclWithExplicitFreight_RequiresMatchingEquipmentDistribution()
    {
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 2, 1 }, new decimal?[] { 2m, 1m }));
        Assert.IsFalse(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 2, 1 }, new decimal?[] { 1m, 1m }));
        Assert.IsFalse(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 2, 1 }, new decimal?[] { 3m }));
        Assert.IsFalse(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 2, 1 }, new decimal?[] { null, 1m }));
    }

    [TestMethod]
    public void FclSingleContainer_PreservesLegacyImplicitFreightQuantity()
    {
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 1 }, new decimal?[] { null }));
        Assert.IsTrue(RateFreightDistributionPolicy.IsValid(
            ShipmentMode.Fcl, new[] { 1 }, new decimal?[] { 0m }));
    }
}
