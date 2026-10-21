namespace Dhole.Pricing.Domain.Rates.Enums;

/// <summary>
/// Air is a legacy alias. All new air-shipment settings use AirConsol, while
/// queries continue matching records persisted with either enum value.
/// </summary>
public static class ShipmentModeNormalization
{
    public static ShipmentMode Normalize(ShipmentMode mode) =>
        mode == ShipmentMode.Air ? ShipmentMode.AirConsol : mode;

    public static bool IsAir(string? modality) =>
        string.Equals(modality?.Trim(), nameof(ShipmentMode.Air), StringComparison.OrdinalIgnoreCase)
        || string.Equals(modality?.Trim(), nameof(ShipmentMode.AirConsol), StringComparison.OrdinalIgnoreCase);

    public static bool AreEquivalent(ShipmentMode first, ShipmentMode second) =>
        Normalize(first) == Normalize(second);

    public static IReadOnlyCollection<ShipmentMode> ExpandAliases(IEnumerable<ShipmentMode> modes)
    {
        var values = modes.Distinct().ToHashSet();
        if (values.Contains(ShipmentMode.Air) || values.Contains(ShipmentMode.AirConsol))
        {
            values.Add(ShipmentMode.Air);
            values.Add(ShipmentMode.AirConsol);
        }

        return values.OrderBy(mode => (int)mode).ToArray();
    }
}
