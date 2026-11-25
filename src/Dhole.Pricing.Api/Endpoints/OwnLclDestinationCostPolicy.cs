namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Explicit cost-matrix scope for MAERSK own-LCL consolidations arriving in Balboa.
/// All other carrier/POE combinations continue using their configured matrix rules.
/// </summary>
public static class OwnLclDestinationCostPolicy
{
    private static readonly HashSet<Guid> MaerskBalboaChargeIds =
    [
        Guid.Parse("396edf76-94d3-4d92-a533-aa6b8501044b"), // Cargos en destino de la Linea Naviera
        Guid.Parse("19cec538-ca34-4ddd-8e56-60b45a1272e4"), // Pase Vacio
        Guid.Parse("523029b2-9303-499c-bb76-c7d48c697709"), // Doc Fee Balboa, Panama
        Guid.Parse("fd5dd670-ba90-44b4-9b49-1ce78d9480b8"), // Inland Balboa -> Colon Free Zone
    ];

    public const int RequiredMaerskBalboaChargeCount = 4;

    public static bool IsMaerskBalboaSelection(IEnumerable<string?> carrierValues, string? arrivalPortCode)
    {
        if (!IsBalboaPort(arrivalPortCode))
            return false;

        return carrierValues.Any(value =>
            Normalize(value) == "NAV2026001" || Normalize(value).StartsWith("MAERSK", StringComparison.Ordinal));
    }

    public static T[] SelectMaerskBalboaCharges<T>(
        IEnumerable<T> candidates,
        Func<T, Guid> costId)
        => candidates.Where(candidate => MaerskBalboaChargeIds.Contains(costId(candidate))).ToArray();

    // Recreated rows have different IDs across databases. Only accept an exact
    // approved semantic name and amount when the original ID is absent.
    // Callers must first filter active, USD, LCL/Any rows by carrier and POE.
    public static T[] ResolveMaerskBalboaCharges<T>(
        IEnumerable<T> candidates, Func<T, Guid> costId,
        Func<T, string> name, Func<T, decimal> amount)
    {
        var rows = candidates.ToArray();
        var required = new (Guid Id, string Name, decimal Amount)[]
        {
            (Guid.Parse("396edf76-94d3-4d92-a533-aa6b8501044b"), "CARGOSENDESTINODELALINEANAVIERA", 342m),
            (Guid.Parse("19cec538-ca34-4ddd-8e56-60b45a1272e4"), "PASEVACIO", 80m),
            (Guid.Parse("523029b2-9303-499c-bb76-c7d48c697709"), "DOCFEEBALBOAPANAMA", 90m),
            (Guid.Parse("fd5dd670-ba90-44b4-9b49-1ce78d9480b8"), "INLANDBALBOACOLONFREEZONE", 400m),
        };
        var selected = new List<T>();
        foreach (var charge in required)
        {
            var exact = rows.Where(row => costId(row) == charge.Id).ToArray();
            if (exact.Length == 0)
                exact = rows.Where(row => !MaerskBalboaChargeIds.Contains(costId(row))
                    && Normalize(name(row)) == charge.Name && amount(row) == charge.Amount).ToArray();
            if (exact.Length != 1)
                return [];
            selected.Add(exact[0]);
        }
        return selected.ToArray();
    }

    public static bool IsBalboaPort(string? value)
        => Normalize(value) is "POE2026003" or "BALBOA" or "BALBOAPANAMA";

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Trim().ToUpperInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsLetterOrDigit).ToArray());
}
