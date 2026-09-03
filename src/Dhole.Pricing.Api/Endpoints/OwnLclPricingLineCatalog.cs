namespace Dhole.Pricing.Api.Endpoints;

internal sealed record OwnLclPricingLineDefinition(
    string LineKey,
    string Scope,
    string Name,
    string ChargeBasis,
    decimal? DefaultCostUnit,
    decimal DefaultSaleUnit);

internal static class OwnLclPricingLineCatalog
{
    public static readonly IReadOnlyList<OwnLclPricingLineDefinition> All =
    [
        new("PA_DESTINATION_CHARGE", "PA", "Destination Charge", "CBM", null, 20m),
        new("PA_DMCE", "PA", "DMCE", "HBL", 65m, 65m),
        new("PA_HANDLING", "PA", "Handling", "HBL", 25m, 25m),
        new("PA_ZONE", "PA", "Zone Charge", "HBL", 30m, 30m),
        new("CR_HANDLING", "CR", "Manejos", "HBL", 65m, 65m),
        new("CR_ZONE", "CR", "Zone Charge", "HBL", 50m, 50m),
        new("CA_DOCUMENTATION", "CA", "Documentación", "HBL", 0m, 65m),
        new("CA_ZONE", "CA", "Zone Charge", "HBL", 0m, 65m),
        new("CA_HANDLING", "CA", "Manejos destino", "HBL", 0m, 50m),
        new("ORIGIN_CFS", "ORIGIN", "CFS", "CBM", 8m, 8m),
        new("ORIGIN_WHSE", "ORIGIN", "WHSE FEE", "CBM", 12m, 12m),
        new("ORIGIN_CUSTOMS", "ORIGIN", "CUSTOMS", "SET", 15m, 25m),
        new("ORIGIN_DOC", "ORIGIN", "DOC FEE", "HBL", 15m, 65m),
        new("ORIGIN_VGM", "ORIGIN", "VGM", "HBL", 0m, 25m),
        new("ORIGIN_MANIFEST", "ORIGIN", "MANIFEST", "HBL", 15m, 25m),
    ];

    public static OwnLclPricingLineDefinition? Find(string? lineKey) =>
        All.FirstOrDefault(item => string.Equals(item.LineKey, lineKey?.Trim(), StringComparison.OrdinalIgnoreCase));
}
