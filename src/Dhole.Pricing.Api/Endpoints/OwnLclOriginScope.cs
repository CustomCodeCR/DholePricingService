namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Shanghai owns the multi-origin China matrix. Every other consolidation is
/// limited to its actual POL and already includes origin freight in its ocean cost.
/// </summary>
public static class OwnLclOriginScope
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CNSHA"] = "SHANGHAI", ["SHANGHAI"] = "SHANGHAI",
            ["CNNGB"] = "NINGBO", ["NINGBO"] = "NINGBO",
            ["CNTAO"] = "QINGDAO", ["CNQNG"] = "QINGDAO", ["QINGDAO"] = "QINGDAO",
            ["CNXMN"] = "XIAMEN", ["XIAMEN"] = "XIAMEN",
            ["CNSWA"] = "SHANTOU", ["SHANTOU"] = "SHANTOU",
            ["CNDLC"] = "DALIAN", ["DALIAN"] = "DALIAN",
            ["CNCKG"] = "CHONGQING", ["CHONGQING"] = "CHONGQING",
            ["CNFOC"] = "FUZHOU", ["FUZHOU"] = "FUZHOU",
            ["CNSZX"] = "SHENZHEN", ["SHENZHEN"] = "SHENZHEN",
            ["CNTSN"] = "XINGANG", ["CNTXG"] = "XINGANG", ["TIANJIN"] = "XINGANG", ["XINGANG"] = "XINGANG",
            ["CNSHK"] = "SHEKOU", ["SHEKOU"] = "SHEKOU",
            ["CNCAN"] = "GUANGZHOU", ["GUANGZHOU"] = "GUANGZHOU",
            ["CNYTN"] = "YANTIAN", ["YANTIAN"] = "YANTIAN",
        };

    public static string Resolve(string? polCode, string? polName) =>
        Normalize(polName) ?? Normalize(polCode) ?? string.Empty;

    public static bool UsesShanghaiMatrix(string? polCode, string? polName) =>
        string.Equals(Resolve(polCode, polName), "SHANGHAI", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesSelectedPol(string? requestedPol, string? polCode, string? polName) =>
        string.IsNullOrWhiteSpace(requestedPol)
        || string.Equals(Normalize(requestedPol), Resolve(polCode, polName), StringComparison.OrdinalIgnoreCase)
        || string.Equals(requestedPol.Trim(), polCode?.Trim(), StringComparison.OrdinalIgnoreCase)
        // Guangzhou embarca desde Yantian. Es la única excepción a la igualdad
        // estricta de POL; ningún consolidado Shanghai se comparte con Yantian.
        || (string.Equals(Normalize(requestedPol), "GUANGZHOU", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Resolve(polCode, polName), "YANTIAN", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyDictionary<string, decimal> ScenarioOrigins(
        string? polCode, string? polName, IReadOnlyDictionary<string, decimal> shanghaiMatrix)
    {
        if (UsesShanghaiMatrix(polCode, polName)) return shanghaiMatrix;
        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [Resolve(polCode, polName)] = 0m,
        };
    }

    public static string? Normalize(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (Aliases.TryGetValue(normalized, out var alias)) return alias;

        // Catalog labels can be "Yantian, Shenzhen, China" while the catalog
        // code itself can be opaque (POL-2026-xxx). Use the actual port name first.
        var portName = normalized.Split(',', '(', '（')[0].Trim();
        if (Aliases.TryGetValue(portName, out alias)) return alias;
        foreach (var port in Aliases)
        {
            if (portName.StartsWith(port.Key + " ", StringComparison.OrdinalIgnoreCase))
                return port.Value;
        }
        return portName;
    }
}
