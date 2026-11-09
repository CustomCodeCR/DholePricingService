using System.Globalization;
using System.Text;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// ALL IN is a commercial/PDF presentation, not a charge.
/// A MultiQUO alternative qualifies only when its own route is FCL
/// with a Panama discharge point and the final POD in Costa Rica.
/// </summary>
public static class PanamaCostaRicaAllInPolicy
{
    public static bool Applies(
        string? shipmentMode,
        string? poeCode,
        string? poeName,
        string? podCode,
        string? podName)
    {
        if (!string.Equals(shipmentMode, "Fcl", StringComparison.OrdinalIgnoreCase))
            return false;

        var poe = Normalize($"{poeCode} {poeName}");
        var pod = Normalize($"{podCode} {podName}");
        var normalizedPoeCode = Normalize(poeCode).Trim();
        var normalizedPodCode = Normalize(podCode).Trim();

        var isPanama = poe.Contains("PANAMA", StringComparison.Ordinal)
            || normalizedPoeCode == "PA"
            || normalizedPoeCode.StartsWith("PA-", StringComparison.Ordinal)
            || (normalizedPoeCode.Length >= 5 && normalizedPoeCode.StartsWith("PA", StringComparison.Ordinal)
                && normalizedPoeCode.All(char.IsLetterOrDigit));

        var isCostaRica = pod.Contains("COSTA RICA", StringComparison.Ordinal)
            || normalizedPodCode == "CR"
            || normalizedPodCode.StartsWith("CR-", StringComparison.Ordinal)
            || (normalizedPodCode.Length >= 5 && normalizedPodCode.StartsWith("CR", StringComparison.Ordinal)
                && normalizedPodCode.All(char.IsLetterOrDigit));

        return isPanama && isCostaRica;
    }

    private static string Normalize(string? source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        var decomposed = source.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                output.Append(char.ToUpperInvariant(character));
        return output.ToString().Normalize(NormalizationForm.FormC);
    }
}
