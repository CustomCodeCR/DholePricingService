using System.Text.RegularExpressions;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// ICC Incoterms® 2020, in force in 2026. A buyer is always the importer
/// and a seller the exporter for cost allocation, regardless of whether
/// Dhole's quote is for Import or Export. Guidance is never a service ban.
/// Destination terminal charges require reference to the named place and
/// the actual transport contract; extraordinary charges are case-specific.
/// </summary>
public static class Incoterm2020Policy
{
    public sealed record Rule(
        string Code,
        bool SeaOrInlandWaterwayOnly,
        string RiskTransfers,
        bool SellerPaysMainCarriage,
        bool SellerMustInsure,
        bool SellerClearsImport,
        bool SellerUnloadsAtNamedPlace);

    private static readonly IReadOnlyDictionary<string, Rule> Rules =
        new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
        {
            ["EXW"] = new("EXW", false, "Disponible sin cargar en origen pactado", false, false, false, false),
            ["FCA"] = new("FCA", false, "Entrega a transportista en lugar pactado", false, false, false, false),
            ["FAS"] = new("FAS", true, "Al costado del buque en origen", false, false, false, false),
            ["FOB"] = new("FOB", true, "A bordo del buque en origen", false, false, false, false),
            ["CFR"] = new("CFR", true, "A bordo del buque en origen", true, false, false, false),
            ["CIF"] = new("CIF", true, "A bordo del buque en origen", true, true, false, false),
            ["CPT"] = new("CPT", false, "Entrega al primer transportista", true, false, false, false),
            ["CIP"] = new("CIP", false, "Entrega al primer transportista", true, true, false, false),
            ["DAP"] = new("DAP", false, "En destino pactado, listo para descargar", true, false, false, false),
            ["DPU"] = new("DPU", false, "Después de descargar en destino pactado", true, false, false, true),
            ["DDP"] = new("DDP", false, "En destino despachado, listo para descargar", true, false, true, false),
        };

    private static readonly Regex RecognizedCode = new(
        @"(?<![A-Za-z])(EXW|FCA|FAS|FOB|CFR|CIF|CPT|CIP|DAP|DPU|DDP)(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static Rule? Describe(string? code, string? label = null)
    {
        // Config codes can be opaque (INC-2026-005); prefer the ICC label.
        foreach (var value in new[] { code, label })
        {
            var match = RecognizedCode.Match(value ?? "");
            if (match.Success && Rules.TryGetValue(match.Value, out var rule))
                return rule;
        }
        return null;
    }

    public static string RecommendPayer(string? term, string chargeCategory)
    {
        var rule = Describe(term);
        if (rule is null) return "ByAgreement";
        return chargeCategory.Trim().ToLowerInvariant() switch
        {
            "mainfreight" => rule.SellerPaysMainCarriage ? "Exporter" : "Importer",
            "exportcustoms" => rule.Code == "EXW" ? "Importer" : "Exporter",
            "importcustoms" or "importtaxes" => rule.SellerClearsImport ? "Exporter" : "Importer",
            "unloading" => rule.SellerUnloadsAtNamedPlace ? "Exporter" : "Importer",
            "insurance" => rule.SellerMustInsure ? "Exporter" : "ByAgreement",
            "pickup" => rule.Code is "EXW" ? "Importer" : rule.Code is "DAP" or "DPU" or "DDP" ? "Exporter" : "ByAgreement",
            "destinationhandling" or "terminal" or "demurrage" or "detention" => "ByAgreement",
            _ => "ByAgreement",
        };
    }

    public static bool RequiresOverrideReason(
        string? term, string chargeCategory, string? agreedPayer)
    {
        var expected = RecommendPayer(term, chargeCategory);
        return expected != "ByAgreement"
            && !string.IsNullOrWhiteSpace(agreedPayer)
            && !string.Equals(expected, agreedPayer, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTransportModeRecommended(string? term, bool seaOrWaterway)
    {
        var rule = Describe(term);
        return rule is null || !rule.SeaOrInlandWaterwayOnly || seaOrWaterway;
    }
}
