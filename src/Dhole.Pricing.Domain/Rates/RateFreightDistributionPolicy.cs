using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Validates equipment-specific freight amounts only when an FCL quotation
/// explicitly includes freight. Incoterms determine contractual responsibility
/// but do not remove selected, billable services.
/// </summary>
public static class RateFreightDistributionPolicy
{
    public static bool IsValid(
        ShipmentMode shipmentMode,
        IReadOnlyCollection<int> containerQuantities,
        IReadOnlyCollection<decimal?> freightQuantities)
    {
        // LTL/LCL freight is priced by chargeable volume, weight or shipment;
        // FTL freight by truck; air by weight. None uses FCL allocations.
        if (shipmentMode != ShipmentMode.Fcl || freightQuantities.Count == 0)
            return true;

        if (containerQuantities.Count == 0 || freightQuantities.Count != containerQuantities.Count)
            return false;
        if (containerQuantities.Any(quantity => quantity <= 0))
            return false;

        // Preserve the legacy one-container quantity fallback when a single
        // freight item has no explicit quantity (null or zero).
        if (containerQuantities.Count == 1 && freightQuantities.First().GetValueOrDefault() == 0m)
            return true;

        if (freightQuantities.Any(quantity => !quantity.HasValue || quantity.Value <= 0m))
            return false;

        return freightQuantities.Sum(quantity => quantity!.Value)
            == containerQuantities.Sum();
    }
}