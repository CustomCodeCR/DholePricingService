using System.Globalization;
using System.Text;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal static class MarketTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToUpperInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static string Compact(string? value) =>
        Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    public static string CanonicalEquipment(string? value)
    {
        var compact = Compact(value);

        return compact switch
        {
            "20GP" or "20STD" or "20DC" or "20DRY" => "20DV",
            "40GP" or "40STD" or "40DC" or "40DRY" => "40DV",
            "40HQ" or "40HIGHCUBE" => "40HC",
            "45HQ" or "45HIGHCUBE" => "45HC",
            _ => compact,
        };
    }

    public static string CanonicalCurrency(string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        if (raw is "$" or "US$" or "US $" or "USD$")
        {
            return "USD";
        }

        if (raw is "₡" or "CRC$" or "CRC ₡")
        {
            return "CRC";
        }

        if (raw == "€")
        {
            return "EUR";
        }

        var normalized = Normalize(value);
        var compact = Compact(value);

        if (compact is "USD" or "DOLAR" or "DOLARES" or "USDOLLAR" or "USDOLLARS")
        {
            return "USD";
        }

        if (compact is "CRC" or "COLON" or "COLONES" or "COLONCOSTARRICENSE" or "COSTARICANCOLON")
        {
            return "CRC";
        }

        if (compact is "EUR" or "EURO" or "EUROS")
        {
            return "EUR";
        }

        if (compact is "CNY" or "RMB" or "YUAN" or "YUANES")
        {
            return "CNY";
        }

        if (normalized.Length == 3 && normalized.All(char.IsLetter))
        {
            return normalized;
        }

        return compact;
    }

    public static string CanonicalCarrier(string? value)
    {
        var compact = Compact(value);

        return compact switch
        {
            "MSK" or "MAEU" or "MAERSKLINE" => "MAERSK",
            "MSCU" or "MSCLINE" => "MSC",
            "OOLU" or "ORIENTOVERSEASCONTAINERLINE" => "OOCL",
            "CMDU" or "CMACGMSA" => "CMACGM",
            "COSU" or "COSCOSHIPPING" => "COSCO",
            "HLCU" or "HAPAGLLOYDAG" => "HAPAGLLOYD",
            "ONEY" or "OCEANNETWORKEXPRESS" => "ONE",
            _ => compact,
        };
    }

    public static string? ExtractIncoterm(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "EXW", "FCA", "FAS", "FOB", "CFR", "CIF",
            "CPT", "CIP", "DAP", "DPU", "DDP"
        };

        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (known.Contains(token))
            {
                return token;
            }
        }

        return normalized.Length == 3 && normalized.All(char.IsLetter)
            ? normalized
            : null;
    }
}
