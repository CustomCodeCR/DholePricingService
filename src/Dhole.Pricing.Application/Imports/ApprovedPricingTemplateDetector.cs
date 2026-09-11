using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Application.Imports;

public static class ApprovedPricingTemplateDetector
{
    private static readonly string[] RequiredColumns =
    [
        "carrier",
        "equipo",
        "cantidad",
        "pol",
        "poe",
        "fleteinternacional",
        "moneda",
        "tipotarifa",
        "etd",
        "validodesde",
        "validohasta",
        "tiempotransitodias",
        "diaslibresendestino",
    ];

    public static bool IsMatch(DataExtractionFclPricingResult extraction)
    {
        return extraction.Rows.Count > 0
            && extraction.Rows.All(row => IsApprovedTemplateRawJson(row.RawJson));
    }

    public static bool IsApprovedTemplateRawJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            CollectKeys(document.RootElement, keys);
            return RequiredColumns.All(keys.Contains);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void CollectKeys(JsonElement element, ISet<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalized = NormalizeKey(property.Name);
                if (normalized.Length > 0)
                {
                    keys.Add(normalized);
                }

                CollectKeys(property.Value, keys);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectKeys(item, keys);
            }
        }
    }

    private static string NormalizeKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
