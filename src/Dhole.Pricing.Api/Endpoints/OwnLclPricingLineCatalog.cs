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
        // Centroamérica según la matriz CNCA: Transbordo, Flete Terrestre,
        // Stuffing, Documentación, Manejos y Manejos en Destino. El flete terrestre
        // varía por país, por eso cada destino conserva su propia línea editable.
        // Costos CNCA-024/#049: el flete marítimo se maneja igual que Panamá.
        // Los componentes adicionales de Centroamérica quedan separados como cargos
        // de destino, exactamente como el HTML de Pricing.
        new("CA_TRANSSHIPMENT", "CA", "Transbordo", "CBM", 39.719736842105264m, 29m), // venta = Destination Charge Panamá + 9
        new("CA_INLAND_NI", "CA", "Flete Terrestre · Nicaragua", "CBM", 16.428571428571429m, 40m),
        new("CA_INLAND_HN", "CA", "Flete Terrestre · Honduras", "CBM", 26.071428571428573m, 50m),
        new("CA_INLAND_GT", "CA", "Flete Terrestre · Guatemala", "CBM", 35m, 48m),
        new("CA_INLAND_SV", "CA", "Flete Terrestre · El Salvador", "CBM", 31.428571428571429m, 40m),
        new("CA_STUFFING", "CA", "Stuffing", "CBM", 5.928571428571429m, 550m / 60m),
        new("CA_DOCUMENTATION", "CA", "Documentación", "HBL", 0m, 185m),
        new("CA_HANDLING", "CA", "Manejos", "HBL", 0m, 45m),
        new("CA_DESTINATION_HANDLING", "CA", "Manejos en Destino", "HBL", 0m, 70m),
        new("ORIGIN_CFS", "ORIGIN", "CFS", "CBM", 8m, 8m),
        new("ORIGIN_WHSE", "ORIGIN", "WHSE FEE", "CBM", 12m, 12m),
        new("ORIGIN_CUSTOMS", "ORIGIN", "CUSTOMS", "SET", 15m, 25m),
        new("ORIGIN_DOC", "ORIGIN", "DOC FEE", "HBL", 15m, 65m),
        new("ORIGIN_VGM", "ORIGIN", "VGM", "HBL", 0m, 25m),
        new("ORIGIN_MANIFEST", "ORIGIN", "MANIFEST", "HBL", 15m, 25m),
        // PICK UP figura como "POR CASO" en el Excel. Se inicializa en cero y se
        // configura por consolidado; solo se aplica cuando la cotización es EXW.
        new("ORIGIN_PICK_UP", "ORIGIN", "PICK UP", "Flat", 0m, 0m),
    ];

    public static OwnLclPricingLineDefinition? Find(string? lineKey) =>
        All.FirstOrDefault(item => string.Equals(item.LineKey, lineKey?.Trim(), StringComparison.OrdinalIgnoreCase));
}
