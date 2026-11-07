namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// Pricing stores the catalog's display Value as CurrencyName and its internal
/// identifier separately as CurrencyCode. Commercial documents must never
/// expose an internal CUR-2026-... identifier as if it were a monetary ISO code.
/// </summary>
public static class QuoteCurrencyValue
{
    public static string Resolve(string? itemValue, string? rateValue)
    {
        foreach (var candidate in new[] { itemValue, rateValue })
        {
            var value = candidate?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (value.StartsWith("CUR-", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("CUR_", StringComparison.OrdinalIgnoreCase))
                continue;
            return value;
        }
        // Never infer USD/CRC from a non-ISO catalog identifier.
        return "—";
    }
}
