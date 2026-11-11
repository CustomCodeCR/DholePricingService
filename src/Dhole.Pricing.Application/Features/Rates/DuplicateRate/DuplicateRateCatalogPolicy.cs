using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

/// <summary>
/// A historical non-FCL quote can contain a synthetic provider or equipment
/// snapshot not registered as a maritime carrier or container type in Config.
/// FCL must continue validating live maritime catalog references.
/// </summary>
public static class DuplicateRateCatalogPolicy
{
    public static readonly Guid OwnLclAgentId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c41");
    public static readonly Guid LandAgentId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c42");
    public static readonly Guid LandCarrierId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c43");
    public static readonly Guid LclCarrierId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c44");

    public static bool IsLand(ShipmentMode mode) => mode is ShipmentMode.Ftl or ShipmentMode.Ltl;

    public static bool IsInternalLclAgent(ShipmentMode mode, Guid? agentId) =>
        mode == ShipmentMode.Lcl && agentId == OwnLclAgentId;

    public static bool RequiresCarrierLookup(ShipmentMode mode, Guid? carrierId) =>
        !IsLand(mode)
        && carrierId is { } id && id != Guid.Empty
        && !(mode == ShipmentMode.Lcl && id == LclCarrierId);

    public static bool RequiresActiveCarrier(ShipmentMode mode) => mode == ShipmentMode.Fcl;
    public static bool CanUseHistoricalEquipmentSnapshot(ShipmentMode mode) => mode != ShipmentMode.Fcl;

    public static IReadOnlyCollection<string> EquipmentCatalogGroups(ShipmentMode mode) => mode switch
    {
        ShipmentMode.Ftl => [
            PricingConstants.CatalogSlugs.LandEquipmentSizes,
            PricingConstants.CatalogSlugs.LandEquipmentTypes,
            PricingConstants.CatalogSlugs.LandEquipmentKinds,
        ],
        ShipmentMode.Ltl => [
            PricingConstants.CatalogSlugs.LandEquipmentTypes,
            PricingConstants.CatalogSlugs.LandEquipmentSizes,
            PricingConstants.CatalogSlugs.LandEquipmentKinds,
        ],
        _ => [PricingConstants.CatalogSlugs.ContainerTypes],
    };

    public static string EquipmentErrorGroup(ShipmentMode mode) => mode switch
    {
        ShipmentMode.Ftl => PricingConstants.CatalogSlugs.LandEquipmentSizes,
        ShipmentMode.Ltl => PricingConstants.CatalogSlugs.LandEquipmentTypes,
        _ => PricingConstants.CatalogSlugs.ContainerTypes,
    };
}
