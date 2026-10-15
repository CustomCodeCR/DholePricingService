using System.Globalization;
using System.Text;

namespace Dhole.Pricing.Domain.Rates.Entities;

public static class RateContainerOverweightPolicy
{
    public const decimal ThresholdKg = 21500m;
    private static string Normalize(string? value)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant();
        return new string(text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
    public static bool IsEligiblePoe(string? poeName)
    {
        var name = Normalize(poeName);
        return !name.Contains("panama") && new[] { "costa rica", "nicaragua", "guatemala",
            "honduras", "el salvador", "belice", "belize" }.Any(name.Contains);
    }
    public static int CountOverweight(string? poeName, IEnumerable<decimal> weightsKg)
        => IsEligiblePoe(poeName) ? weightsKg.Count(w => w > ThresholdKg) : 0;
    public static bool IsOverweightDetail(string? name)
    {
        var text = Normalize(name);
        return text.Contains("sobrepeso") || text.Contains("overweight");
    }
}
