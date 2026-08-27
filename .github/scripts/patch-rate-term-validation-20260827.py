from pathlib import Path
import re

path = Path('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs')
text = path.read_text(encoding='utf-8')

pattern = re.compile(
    r"    private static IResult\? ValidateExclusiveRateTerms\([\s\S]*?\n"
    r"    private static async Task<int> ResolveConfiguredFreeDaysAsync",
    re.MULTILINE,
)

replacement = r'''    private static IResult? ValidateExclusiveRateTerms(
        string? includes,
        string? subjectTo,
        string? excludes,
        HttpContext httpContext
    )
    {
        static HashSet<string> Lines(string? value) =>
            (value ?? string.Empty)
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x =>
                {
                    var normalized = System.Text.RegularExpressions.Regex.Replace(
                        x.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " "
                    ).Trim();
                    var qualifier = System.Text.RegularExpressions.Regex.Match(
                        normalized, @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)"
                    );
                    return qualifier.Success && qualifier.Index > 0
                        ? normalized[..qualifier.Index].Trim()
                        : normalized;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Repetir accidentalmente una línea dentro de la misma categoría no viola
        // exclusividad. El error aplica únicamente si el mismo ítem aparece en
        // categorías distintas: Incluye / Sujeto a / No incluye.
        var categories = new[] { Lines(includes), Lines(subjectTo), Lines(excludes) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            foreach (var item in category)
            {
                if (seen.Contains(item))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.RateTermItemDuplicated",
                        "Un ítem de tarifa solo puede pertenecer a una categoría de la cotización.",
                        httpContext
                    );
                }
            }

            seen.UnionWith(category);
        }
        return null;
    }

    private static async Task<int> ResolveConfiguredFreeDaysAsync'''

text, count = pattern.subn(lambda _: replacement, text, count=1)
if count != 1:
    raise SystemExit(f'ValidateExclusiveRateTerms no reemplazado: {count}')

path.write_text(text, encoding='utf-8')
print('Validador de categorías corregido.')
