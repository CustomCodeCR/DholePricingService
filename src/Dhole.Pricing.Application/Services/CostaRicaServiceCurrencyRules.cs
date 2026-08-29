using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Services;

internal static class CostaRicaServiceCurrencyRules
{
    private static readonly string[] ForcedCrcServiceNames =
    [
        "agencia de aduanas crc",
        "agencia aduanas crc",
        "almacenamiento",
        "embalaje de carga",
        "picking cargas",
        "recepcion de carga",
        "transporte de entrega",
        "transporte de recoleccion",
    ];

    public static bool IsCostaRicaContext(RateHeader rate) =>
        IsCostaRicaContext(rate.OperationType, rate.PoeCode, rate.PoeName);

    public static bool IsCostaRicaContext(
        RateOperationType operationType,
        string? poeCode,
        string? poeName
    )
    {
        if (operationType == RateOperationType.Import)
            return true;

        var code = (poeCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code == "CR" || Regex.IsMatch(code, "^CR[A-Z0-9]{3}$", RegexOptions.CultureInvariant))
            return true;

        return Normalize($"{poeCode} {poeName}").Contains("costa rica", StringComparison.Ordinal);
    }

    public static bool RequiresCrc(Cost cost, RateHeader rate) =>
        IsCostaRicaContext(rate) && RequiresCrc(cost);

    public static bool RequiresCrc(Cost cost)
    {
        return cost.Services.Any(service =>
        {
            var serviceText = Normalize($"{service.ServiceName} {service.ServiceCode}");
            return ForcedCrcServiceNames.Any(forced =>
                serviceText.Equals(forced, StringComparison.Ordinal)
                || serviceText.Contains(forced, StringComparison.Ordinal));
        });
    }

    public static decimal ConvertUsdCrc(
        decimal amount,
        string sourceCurrencyCode,
        string targetCurrencyCode,
        decimal exchangeRateSale
    )
    {
        var source = CanonicalCurrencyCode(sourceCurrencyCode);
        var target = CanonicalCurrencyCode(targetCurrencyCode);

        if (source == target)
            return amount;

        if (exchangeRateSale <= 0m)
            throw new InvalidOperationException("El tipo de cambio USD/CRC debe ser mayor que cero.");

        return (source, target) switch
        {
            ("USD", "CRC") => decimal.Round(amount * exchangeRateSale, 2, MidpointRounding.AwayFromZero),
            ("CRC", "USD") => decimal.Round(amount / exchangeRateSale, 2, MidpointRounding.AwayFromZero),
            _ => throw new InvalidOperationException(
                $"No se puede convertir automáticamente la moneda {sourceCurrencyCode} a {targetCurrencyCode}."
            ),
        };
    }

    public static string CanonicalCurrencyCode(string? code)
    {
        var raw = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (raw is "USD" or "CRC")
            return raw;

        var normalized = Normalize(raw);
        if (normalized.Contains("usd", StringComparison.Ordinal)
            || normalized.Contains("dolar", StringComparison.Ordinal)
            || normalized.Contains("dollar", StringComparison.Ordinal))
            return "USD";
        if (normalized.Contains("crc", StringComparison.Ordinal)
            || normalized.Contains("colon", StringComparison.Ordinal))
            return "CRC";

        return raw;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
