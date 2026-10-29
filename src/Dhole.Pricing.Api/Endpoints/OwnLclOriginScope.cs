namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Shanghai owns the multi-origin China matrix. Every other consolidation is
/// limited to its actual POL and already includes origin freight in its ocean cost.
/// </summary>
public static class OwnLclOriginScope
{
    /// <summary>
    /// Única matriz de diferenciales de origen para consolidados físicos Shanghai.
    /// La tarifa final añade el diferencial del POL comercial a la base del
    /// consolidado y a los cargos del país de destino.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> ShanghaiOriginSurcharges { get; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHANGHAI"] = 0m,
            ["NINGBO"] = 52m,
            ["QINGDAO"] = 52m,
            ["XIAMEN"] = 57m,
            ["SHANTOU"] = 57m,
            ["DALIAN"] = 57m,
            ["CHONGQING"] = 57m,
            ["FUZHOU"] = 57m,
            ["SHENZHEN"] = 62m,
            ["XINGANG"] = 62m,
            ["SHEKOU"] = 62m,
            ["GUANGZHOU"] = 62m,
        };

    /// <summary>
    /// Resuelve el POL de la cotización y su fila de tarifa. Nunca se toma el
    /// diferencial SHANGHAI=0 para un POL que no se pueda reconocer.
    /// </summary>
    public static bool TryMatchShanghaiRate(
        string? selectedPol, out string commercialPol, out string matrixPol, out decimal surcharge)
    {
        commercialPol = Normalize(selectedPol) ?? "SHANGHAI";
        matrixPol = ShanghaiRateOrigin(commercialPol);
        return ShanghaiOriginSurcharges.TryGetValue(matrixPol, out surcharge);
    }

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
        // Guangzhou también puede utilizar el consolidado propio de Yantian.
        // Las matrices de Shanghai se validan aparte por el POL comercial.
        || (string.Equals(Normalize(requestedPol), "GUANGZHOU", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Resolve(polCode, polName), "YANTIAN", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Yantian (Shenzhen) is priced with the existing SHENZHEN differential only
    /// when a physical Shanghai consolidation is selected. Never use this
    /// alias to change the POL of a Yantian-owned consolidation.
    /// </summary>
    public static string ShanghaiRateOrigin(string? requestedPol)
    {
        var normalized = Normalize(requestedPol) ?? string.Empty;
        return string.Equals(normalized, "YANTIAN", StringComparison.OrdinalIgnoreCase)
            ? "SHENZHEN"
            : normalized;
    }

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
