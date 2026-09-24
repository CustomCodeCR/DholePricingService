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
    public const string ChinaMatrix = "CHINA";
    public const string MiamiMatrix = "MIAMI";

    public static readonly IReadOnlyList<OwnLclPricingLineDefinition> China =
    [
        new("PA_DESTINATION_CHARGE", "PA", "Destination Charge", "CBM", null, 20m),
        new("PA_DMCE", "PA", "DMCE", "HBL", 65m, 65m),
        new("PA_HANDLING", "PA", "Handling", "HBL", 25m, 25m),
        new("PA_ZONE", "PA", "Zone Charge", "HBL", 30m, 30m),
        new("CR_HANDLING", "CR", "Manejos", "HBL", 0m, 65m),
        new("CR_ZONE", "CR", "Zone Charge", "HBL", 0m, 50m),

        // Matriz China / Centroamérica. Cada valor sigue siendo editable por consolidado.
        new("CA_TRANSSHIPMENT", "CA", "Transbordo", "CBM", 9m, 29m),
        new("CA_INLAND_NI", "CA", "Flete Terrestre · Nicaragua", "CBM", 1150m, 40m),
        new("CA_INLAND_HN", "CA", "Flete Terrestre · Honduras", "CBM", 1825m, 50m),
        new("CA_INLAND_GT", "CA", "Flete Terrestre · Guatemala", "CBM", 2450m, 48m),
        new("CA_INLAND_SV", "CA", "Flete Terrestre · El Salvador", "CBM", 2200m, 40m),
        new("CA_STUFFING", "CA", "Stuffing", "CBM", 550m / 60m, 10m),
        new("CA_DOCUMENTATION", "CA", "Documentación", "HBL", 0m, 185m),
        new("CA_HANDLING", "CA", "Manejos", "HBL", 0m, 45m),
        new("CA_DESTINATION_HANDLING", "CA", "Manejos en Destino", "HBL", 0m, 70m),

        // Origen FCA / EXW exclusivo de la matriz China.
        new("ORIGIN_CFS", "ORIGIN", "CFS", "CBM", 8m, 8m),
        new("ORIGIN_WHSE", "ORIGIN", "WHSE FEE", "CBM", 12m, 12m),
        new("ORIGIN_CUSTOMS", "ORIGIN", "CUSTOMS", "SET", 15m, 25m),
        new("ORIGIN_DOC", "ORIGIN", "DOC FEE", "HBL", 15m, 65m),
        new("ORIGIN_VGM", "ORIGIN", "VGM", "HBL", 0m, 25m),
        new("ORIGIN_MANIFEST", "ORIGIN", "MANIFEST", "HBL", 15m, 25m),
        new("ORIGIN_PICK_UP", "ORIGIN", "PICK UP", "Flat", 0m, 0m),
    ];

    // Miami NO comparte la matriz China. Estos son los conceptos base del proyecto
    // Miami y se guardan por consolidado. Bunker y THC/D arrancan en cero porque
    // dependen del tarifario/proyecto que se esté cargando.
    public static readonly IReadOnlyList<OwnLclPricingLineDefinition> Miami =
    [
        new("MIA_HANDLING", "MIA", "Manejos", "HBL", 0m, 45m),
        new("MIA_FORWARDING", "MIA", "Forwarding", "HBL", 0m, 50m),
        new("MIA_HBL", "MIA", "HBL", "HBL", 0m, 40m),
        new("MIA_BUNKER", "MIA", "Bunker", "CBM", 0m, 0m),
        new("MIA_THCD", "MIA", "THC/D", "CBM", 0m, 0m),
    ];

    public static readonly IReadOnlyList<OwnLclPricingLineDefinition> All =
        China.Concat(Miami).ToArray();

    public static IReadOnlyList<OwnLclPricingLineDefinition> ForOrigin(string? polCode, string? polName) =>
        IsMiamiOrigin(polCode, polName) ? Miami : China;

    public static string MatrixCode(string? polCode, string? polName) =>
        IsMiamiOrigin(polCode, polName) ? MiamiMatrix : ChinaMatrix;

    public static string MatrixVersionPrefix(string? polCode, string? polName) =>
        IsMiamiOrigin(polCode, polName) ? "MIA" : "CNCA";

    public static string DefaultConsolidationName(string? polCode, string? polName, int consolidationNumber) =>
        IsMiamiOrigin(polCode, polName)
            ? $"Consolidado Miami {consolidationNumber}"
            : $"Consolidado China {consolidationNumber}";

    public static bool IsMiamiOrigin(string? polCode, string? polName)
    {
        foreach (var value in new[] { polCode, polName })
        {
            var compact = Compact(value);
            if (compact.Length == 0) continue;
            if (compact is "MIA" or "USMIA" || compact.Contains("MIAMI", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static OwnLclPricingLineDefinition? Find(string? lineKey) =>
        All.FirstOrDefault(item => string.Equals(item.LineKey, lineKey?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Compact(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
