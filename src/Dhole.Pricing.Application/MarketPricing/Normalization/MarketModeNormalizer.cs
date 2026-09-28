using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal sealed class MarketModeNormalizer
{
    public ShipmentMode? Normalize(ShipmentMode? hint, string? rawMode)
    {
        if (hint.HasValue && Enum.IsDefined(hint.Value))
        {
            return hint.Value;
        }

        var normalized = MarketTextNormalizer.Normalize(rawMode);
        var compact = MarketTextNormalizer.Compact(rawMode);

        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (
            compact.Contains("AIRCONSOL", StringComparison.Ordinal)
            || normalized.Contains("AIR CONSOL", StringComparison.Ordinal)
            || normalized.Contains("AEREO CONSOL", StringComparison.Ordinal)
            || normalized.Contains("AÉREO CONSOL", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.AirConsol;
        }

        if (
            compact == "AIR"
            || normalized.Contains("AIR FREIGHT", StringComparison.Ordinal)
            || normalized.Contains("AEREO", StringComparison.Ordinal)
            || normalized.Contains("AERÉO", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.Air;
        }

        if (
            compact.Contains("FCL", StringComparison.Ordinal)
            || normalized.Contains("FULL CONTAINER", StringComparison.Ordinal)
            || normalized.Contains("MARITIMO COMPLETO", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.Fcl;
        }

        if (
            compact.Contains("LCL", StringComparison.Ordinal)
            || normalized.Contains("LESS CONTAINER", StringComparison.Ordinal)
            || normalized.Contains("CONSOLIDADO MARITIMO", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.Lcl;
        }

        if (
            compact.Contains("FTL", StringComparison.Ordinal)
            || normalized.Contains("FULL TRUCK", StringComparison.Ordinal)
            || normalized.Contains("CAMION COMPLETO", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.Ftl;
        }

        if (
            compact.Contains("LTL", StringComparison.Ordinal)
            || normalized.Contains("LESS TRUCK", StringComparison.Ordinal)
            || normalized.Contains("CARGA PARCIAL TERRESTRE", StringComparison.Ordinal)
        )
        {
            return ShipmentMode.Ltl;
        }

        return null;
    }
}
