using System.Text.RegularExpressions;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Normalizes the operational IDTRA identifier used in Dynamics lookups.
/// Quote references following -QUO or // are not part of an IDTRA.
/// Never infer an incident from a partial or ambiguous identifier.
/// </summary>
public static class IdtraNumberNormalizer
{
    public const string Prefix = "IDTRA999.";

    private static readonly Regex QuoteSuffix = new(
        @"\s*[-–—]\s*QUO(?=[\s\-#0-9]|$).*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ValidBody = new(
        @"^[0-9][0-9A-Za-z._-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string[] Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];
        var identifiers = source.Split(["//"], 2, StringSplitOptions.None)[0]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeOne)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return identifiers;
    }

    public static string? NormalizeCsvOrNull(string? source)
    {
        var values = Parse(source);
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private static string NormalizeOne(string source)
    {
        var value = QuoteSuffix.Replace(source.Trim(), "").Trim();

        // A display label may be repeated: "IDTRA IDTRA999.12589".
        while (Regex.IsMatch(value, @"^IDTRA\s+(?=IDTRA)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            value = Regex.Replace(value, @"^IDTRA\s+", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        string body;
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            body = value[Prefix.Length..];
        else
        {
            body = Regex.Replace(value, @"^IDTRA\s*[:.\-]?\s*", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (body.StartsWith("999.", StringComparison.OrdinalIgnoreCase))
                body = body[4..];
        }

        body = body.Trim();
        if (body.Length == 0 || body.Length > 94 || !ValidBody.IsMatch(body))
            throw new InvalidOperationException(
                "El IDTRA debe contener un número válido. No incluya la QUO ni otras referencias.");
        return Prefix + body;
    }
}
