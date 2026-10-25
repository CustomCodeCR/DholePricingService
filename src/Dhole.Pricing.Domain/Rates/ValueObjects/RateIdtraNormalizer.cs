using System.Text.RegularExpressions;

namespace Dhole.Pricing.Domain.Rates.ValueObjects;

/// <summary>
/// Canonical list of actual IDTRA identifiers. Commercial quote references
/// appended as "-QUO11098", "-QUO 11098" or " // QUO-..." never belong
/// in a Dynamics incident lookup or the persisted IDTRA-only field.
/// Hyphens inside real identifiers (e.g. IDTRA-2026-00125) are preserved.
/// </summary>
public static class RateIdtraNormalizer
{
    private static readonly Regex LeadingDisplayLabel = new(
        @"^IDTRA(?:\s+|\s*:\s*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Stop only at a QUO reference, not at a legitimate identifier hyphen.
    // The digit lookahead covers IDs like IDTRA12589-QUO11098.
    private static readonly Regex QuoteSuffix = new(
        @"\s*[-–—]\s*QUO(?=\s|[-#]|[0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string[] Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var idtraSegment = raw.Split(["//"], 2, StringSplitOptions.None)[0];
        return idtraSegment.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(item => LeadingDisplayLabel.Replace(item, "").Trim())
            .Select(item => QuoteSuffix.Split(item, 2)[0].Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? NormalizeCsv(string? raw)
    {
        var identifiers = Parse(raw);
        return identifiers.Length == 0 ? null : string.Join(", ", identifiers);
    }
}
