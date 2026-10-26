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
        var port = Normalize(arrivalPortCode);
        if (port is not ("POE2026003" or "BALBOA" or "BALBOAPANAMA"))
            return false;

        return carrierValues.Any(value =>
            Normalize(value).StartsWith("MAERSK", StringComparison.Ordinal));
    }

    public static T[] SelectMaerskBalboaCharges<T>(
        IEnumerable<T> candidates,
        Func<T, Guid> costId)
        => candidates.Where(candidate => MaerskBalboaChargeIds.Contains(costId(candidate))).ToArray();

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Trim().ToUpperInvariant()
            .Where(char.IsLetterOrDigit).ToArray());
}
