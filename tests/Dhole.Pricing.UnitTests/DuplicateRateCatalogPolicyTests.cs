using Dhole.Pricing.Application.Features.Rates.DuplicateRate;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class DuplicateRateCatalogPolicyTests
{
    [TestMethod]
    public void Fcl_StillRequiresValidMaritimeCarrierAndEquipment()
    {
        Assert.IsTrue(DuplicateRateCatalogPolicy.RequiresActiveCarrier(ShipmentMode.Fcl));
        Assert.IsTrue(DuplicateRateCatalogPolicy.RequiresCarrierLookup(ShipmentMode.Fcl, Guid.NewGuid()));
        Assert.IsFalse(DuplicateRateCatalogPolicy.CanUseHistoricalEquipmentSnapshot(ShipmentMode.Fcl));
        CollectionAssert.AreEqual(
            new[] { PricingConstants.CatalogSlugs.ContainerTypes },
            DuplicateRateCatalogPolicy.EquipmentCatalogGroups(ShipmentMode.Fcl).ToArray());
    }

    [TestMethod]
    public void Lcl_SyntheticAgentAndCarrierMustNotBeQueriedInConfig()
    {
        Assert.IsTrue(DuplicateRateCatalogPolicy.IsInternalLclAgent(
            ShipmentMode.Lcl, DuplicateRateCatalogPolicy.OwnLclAgentId));
        Assert.IsFalse(DuplicateRateCatalogPolicy.RequiresCarrierLookup(
            ShipmentMode.Lcl, DuplicateRateCatalogPolicy.LclCarrierId));
        Assert.IsFalse(DuplicateRateCatalogPolicy.RequiresActiveCarrier(ShipmentMode.Lcl));
        Assert.IsTrue(DuplicateRateCatalogPolicy.CanUseHistoricalEquipmentSnapshot(ShipmentMode.Lcl));
    }

    [TestMethod]
    [DataRow(ShipmentMode.Ftl)]
    [DataRow(ShipmentMode.Ltl)]
    public void Land_DoesNotRequireMaritimeCarrier_AndUsesLandEquipment(ShipmentMode mode)
    {
        Assert.IsTrue(DuplicateRateCatalogPolicy.IsLand(mode));
        Assert.IsFalse(DuplicateRateCatalogPolicy.RequiresCarrierLookup(
            mode, DuplicateRateCatalogPolicy.LandCarrierId));
        Assert.IsFalse(DuplicateRateCatalogPolicy.RequiresActiveCarrier(mode));
        Assert.IsTrue(DuplicateRateCatalogPolicy.EquipmentCatalogGroups(mode)
            .Contains(PricingConstants.CatalogSlugs.LandEquipmentTypes));
        Assert.IsTrue(DuplicateRateCatalogPolicy.CanUseHistoricalEquipmentSnapshot(mode));
    }

    [TestMethod]
    [DataRow(ShipmentMode.Air)]
    [DataRow(ShipmentMode.AirConsol)]
    public void Air_DoesNotRequireActiveMaritimeCarrier(ShipmentMode mode)
    {
        Assert.IsFalse(DuplicateRateCatalogPolicy.IsLand(mode));
        Assert.IsFalse(DuplicateRateCatalogPolicy.RequiresActiveCarrier(mode));
        Assert.IsTrue(DuplicateRateCatalogPolicy.CanUseHistoricalEquipmentSnapshot(mode));
    }
}
