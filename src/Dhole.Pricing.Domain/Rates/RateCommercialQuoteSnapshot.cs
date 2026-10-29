using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates;

/// <summary>
/// The sole commercial projection of persisted quote details. API views, exports,
/// totals, and customer documents consume these figures rather than applying
/// their own unit/weight/volume/tax mathematics.
/// </summary>
public sealed record RateCommercialMiamiBreakdownLine(string Name, decimal Rate, decimal Quantity, decimal CostAmount);

public sealed record RateCommercialLineSnapshot(
    Guid DetailId,
    decimal Quantity,
    decimal UnitCost,
    decimal UnitSale,
    decimal CostSubtotal,
    decimal SaleSubtotal,
    decimal TaxAmount,
    decimal SaleTotal,
    bool ShowOnClientPdf
)
{
    public decimal UtilitySubtotal => SaleSubtotal - CostSubtotal;
    public IReadOnlyList<RateCommercialMiamiBreakdownLine> MiamiBreakdown { get; init; } = [];
}

public sealed record RateCommercialCurrencyTotal(
    string Currency,
    string CurrencyCode,
    decimal Subtotal,
    decimal Tax,
    decimal Amount
);

public sealed class RateCommercialQuoteSnapshot
{
    private const decimal PoundsPerKg = 2.20462262m;
    private const decimal CftPerCbm = 35.3146667m;
    private const decimal MiamiKgPerCft = 14.16m;

    private readonly IReadOnlyDictionary<Guid, RateCommercialLineSnapshot> _lines;
    public IReadOnlyDictionary<Guid, RateCommercialLineSnapshot> Lines => _lines;
    public IReadOnlyList<RateCommercialCurrencyTotal> CurrencyTotals { get; }
    public bool UseAllIn { get; }
    public bool IsAirLcl { get; }
    public bool HasEquivalentCurrencies { get; }
    public decimal? AppliedExchangeRate { get; }
    public decimal AllInSubtotal { get; }
    public decimal? MiamiBillableCft { get; }
    public decimal AllInTax { get; }
    public decimal AllInTotal => AllInSubtotal + AllInTax;

    private RateCommercialQuoteSnapshot(
        IReadOnlyDictionary<Guid, RateCommercialLineSnapshot> lines,
        IReadOnlyList<RateCommercialCurrencyTotal> currencyTotals,
        bool useAllIn,
        bool isAirLcl,
        bool hasEquivalentCurrencies,
        decimal? appliedExchangeRate,
        decimal allInSubtotal,
        decimal allInTax,
        decimal? miamiBillableCft)
    {
        _lines = lines;
        CurrencyTotals = currencyTotals;
        UseAllIn = useAllIn;
        IsAirLcl = isAirLcl;
        HasEquivalentCurrencies = hasEquivalentCurrencies;
        AppliedExchangeRate = appliedExchangeRate;
        AllInSubtotal = allInSubtotal;
        AllInTax = allInTax;
        MiamiBillableCft = miamiBillableCft;
    }

    public RateCommercialLineSnapshot For(RateDetail detail) => _lines[detail.Id];

    public static RateCommercialQuoteSnapshot Create(RateHeader rate)
    {
        var isAirLcl = DetectAirLcl(rate);
        var miamiCft = CalculateLegacyMiamiBillableCft(rate);
        var projections = new Dictionary<Guid, RateCommercialLineSnapshot>();

        foreach (var detail in rate.RateDetails)
        {
            var quantity = detail.Quantity;
            var unitSale = detail.SaleAmount;
            var unitCost = detail.CostAmount;

            // Historical flat pickup rows may carry a volume basis despite being
            // a single explicitly selected charge.
            if (IsPickup(detail))
            {
                quantity = 1m;
            }
            else if (isAirLcl && detail.ChargeBasis == ChargeBasis.PerLb && rate.TotalWeightKg > 0m)
            {
                // Present the price per KG and billable KG together; the
                // persisted source tariff remains priced per pound.
                quantity = rate.TotalWeightKg;
                unitSale *= PoundsPerKg;
                unitCost *= PoundsPerKg;
            }
            else if (miamiCft.HasValue
                && detail.CostDetailType == CostDetailType.Freight
                && detail.ChargeBasis == ChargeBasis.PerChargeableCft)
            {
                var previousCft = Math.Max(rate.ChargeableQuantity, 1m) * CftPerCbm;
                if (Math.Abs(quantity - previousCft) < 0.05m
                    && miamiCft.Value + 0.05m < quantity)
                    quantity = miamiCft.Value;
            }

            var subtotal = unitSale * quantity;
            var tax = detail.ApplyDestinationTax && detail.DestinationTaxRate > 0m
                ? decimal.Round(subtotal * detail.DestinationTaxRate / 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
            projections.Add(detail.Id, new RateCommercialLineSnapshot(
                detail.Id, quantity, unitCost, unitSale,
                unitCost * quantity, subtotal, tax, subtotal + tax,
                detail.SaleAmount > 0m)
            {
                MiamiBreakdown = CalculateMiamiBreakdown(detail, quantity, miamiCft)
            });
        }

        // A zero-sale line remains in the internal quote and Excel but cannot
        // appear in the customer-facing PDF, ALL IN or currency totals.
        var visible = rate.RateDetails.Where(detail => projections[detail.Id].ShowOnClientPdf).ToArray();
        var target = CurrencyIso(rate.CurrencyCode, rate.CurrencyName);
        var fx = rate.ExchangeRateApplied is > 0m ? rate.ExchangeRateApplied : rate.ExchangeRateSale;
        var useAllIn = rate.UseAllInPresentation && visible.Length > 0
            && visible.All(detail => CanConvert(CurrencyIso(detail.CurrencyCode, detail.CurrencyName), target, fx));

        decimal Convert(decimal amount, string from)
        {
            if (string.Equals(from, target, StringComparison.OrdinalIgnoreCase)) return amount;
            if (fx is > 0m && from == "USD" && target == "CRC") return amount * fx.Value;
            if (fx is > 0m && from == "CRC" && target == "USD") return amount / fx.Value;
            throw new InvalidOperationException("No existe una conversión válida para los importes ALL IN.");
        }

        var allInSubtotal = useAllIn
            ? decimal.Round(visible.Sum(detail => Convert(
                projections[detail.Id].SaleSubtotal, CurrencyIso(detail.CurrencyCode, detail.CurrencyName))),
                2, MidpointRounding.AwayFromZero)
            : 0m;
        var allInTax = useAllIn
            ? decimal.Round(visible.Sum(detail => Convert(
                projections[detail.Id].TaxAmount, CurrencyIso(detail.CurrencyCode, detail.CurrencyName))),
                2, MidpointRounding.AwayFromZero)
            : 0m;

        var totals = useAllIn
            ? new List<RateCommercialCurrencyTotal>
            {
                new(CurrencyDisplay(rate.CurrencyName, rate.CurrencyCode), rate.CurrencyCode,
                    allInSubtotal, allInTax, allInSubtotal + allInTax)
            }
            : visible.GroupBy(detail => CurrencyDisplay(detail.CurrencyName, detail.CurrencyCode).Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    var subtotal = group.Sum(detail => projections[detail.Id].SaleSubtotal);
                    var tax = group.Sum(detail => projections[detail.Id].TaxAmount);
                    var code = Regex.IsMatch(group.Key, "^[A-Z]{3}$") ? group.Key : first.CurrencyCode;
                    return new RateCommercialCurrencyTotal(
                        CurrencyDisplay(first.CurrencyName, first.CurrencyCode),
                        code, subtotal, tax, subtotal + tax);
                }).OrderBy(group => group.CurrencyCode).ThenBy(group => group.Currency).ToList();

        var hasEquivalent = visible.Length > 0 && fx is > 0m && (useAllIn
            ? target is "USD" or "CRC"
            : visible.All(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) is "USD" or "CRC"));
        if (hasEquivalent)
        {
            var usdSubtotal = useAllIn
                ? (target == "USD" ? allInSubtotal : 0m)
                : visible.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "USD")
                    .Sum(detail => projections[detail.Id].SaleSubtotal);
            var crcSubtotal = useAllIn
                ? (target == "CRC" ? allInSubtotal : 0m)
                : visible.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "CRC")
                    .Sum(detail => projections[detail.Id].SaleSubtotal);
            var usdTax = useAllIn
                ? (target == "USD" ? allInTax : 0m)
                : visible.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "USD")
                    .Sum(detail => projections[detail.Id].TaxAmount);
            var crcTax = useAllIn
                ? (target == "CRC" ? allInTax : 0m)
                : visible.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "CRC")
                    .Sum(detail => projections[detail.Id].TaxAmount);

            var usdEquivalent = decimal.Round(usdSubtotal + crcSubtotal / fx!.Value,
                2, MidpointRounding.AwayFromZero);
            var crcEquivalent = decimal.Round(crcSubtotal + usdSubtotal * fx.Value,
                2, MidpointRounding.AwayFromZero);
            var usdTaxEquivalent = decimal.Round(usdTax + crcTax / fx.Value,
                2, MidpointRounding.AwayFromZero);
            var crcTaxEquivalent = decimal.Round(crcTax + usdTax * fx.Value,
                2, MidpointRounding.AwayFromZero);
            totals =
            [
                new("USD", "USD", usdEquivalent, usdTaxEquivalent, usdEquivalent + usdTaxEquivalent),
                new("CRC", "CRC", crcEquivalent, crcTaxEquivalent, crcEquivalent + crcTaxEquivalent)
            ];
        }

        return new RateCommercialQuoteSnapshot(projections, totals, useAllIn,
            isAirLcl, hasEquivalent, fx, allInSubtotal, allInTax, miamiCft);
    }

    private static IReadOnlyList<RateCommercialMiamiBreakdownLine> CalculateMiamiBreakdown(
        RateDetail detail, decimal quantity, decimal? billableCft)
    {
        if (!billableCft.HasValue
            || detail.CostDetailType != CostDetailType.Freight
            || detail.ChargeBasis != ChargeBasis.PerChargeableCft
            || (!detail.Name.Contains("Flete Miami", StringComparison.OrdinalIgnoreCase)
                && detail.Notes?.Contains("Plan Miami:", StringComparison.OrdinalIgnoreCase) != true))
            return [];

        var plan = Regex.Match(detail.Notes ?? string.Empty,
            @"Plan Miami:\s*(NVOCC-A|NVOCC-B|LITTLE|A|B|C|D)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!plan.Success) return [];
        (string Name, decimal Rate)[] parts = plan.Groups[1].Value.ToUpperInvariant() switch
        {
            "A" => [("Flete", 1.75m), ("Bunker", 0.70m), ("THC", 0.35m)],
            "B" => [("Flete", 1.80m), ("Bunker", 0.80m), ("THC", 0.35m)],
            "C" => [("Flete", 1.85m), ("Bunker", 0.80m), ("THC", 0.35m)],
            "D" => [("Flete", 1.85m), ("Bunker", 0.72m), ("THC", 0.35m)],
            "NVOCC-B" => [("Flete", 1.85m), ("Bunker", 0.70m), ("THC", 0.35m)],
            "NVOCC-A" => [("Flete", 2.04m), ("GRI", 0.22m), ("THC", 0.34m)],
            _ => []
        };
        var totalRate = parts.Sum(part => part.Rate);
        if (quantity <= 0m || totalRate <= 0m) return [];

        var cost = detail.CostAmount * quantity;
        decimal allocated = 0m;
        var lines = new List<RateCommercialMiamiBreakdownLine>(parts.Length);
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var lineCost = index == parts.Length - 1
                ? decimal.Round(cost - allocated, 2, MidpointRounding.AwayFromZero)
                : decimal.Round(cost * part.Rate / totalRate, 2, MidpointRounding.AwayFromZero);
            allocated += lineCost;
            lines.Add(new(part.Name, part.Rate, quantity, lineCost));
        }
        return lines;
    }

    private static bool CanConvert(string from, string to, decimal? exchangeRate) =>
        string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
        || exchangeRate is > 0m && (from is "USD" or "CRC") && (to is "USD" or "CRC");

    private static string CurrencyDisplay(string? name, string? code) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim()
        : !string.IsNullOrWhiteSpace(code) ? code.Trim() : "USD";

    private static string CurrencyIso(string? code, string? name)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        var n = (name ?? string.Empty).Trim().ToUpperInvariant();
        if (c is "USD" or "CRC") return c;
        if (n.Contains("USD", StringComparison.Ordinal)) return "USD";
        if (n.Contains("CRC", StringComparison.Ordinal)
            || n.Contains("COLON", StringComparison.Ordinal)
            || n.Contains("COLÓN", StringComparison.Ordinal)) return "CRC";
        return c;
    }

    private static bool IsPickup(RateDetail detail)
    {
        var name = detail.Name.Trim();
        return name.StartsWith("Recolecta", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Recolección", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Pickup", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Pick up", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetectAirLcl(RateHeader rate) =>
        rate.ShipmentMode == ShipmentMode.Lcl
        && (rate.RateDetails.Any(detail => detail.CostDetailType == CostDetailType.Freight
                && detail.ChargeBasis is ChargeBasis.PerKg or ChargeBasis.PerLb or ChargeBasis.Per100Kg)
            || Regex.IsMatch(rate.CargoLinesJson ?? string.Empty,
                @"a[eé]re[ao]|aviaci[oó]n|aeropuerto|airport|vuelos?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || (Regex.IsMatch(rate.PolName ?? string.Empty, @"\([A-Z]{3}\)")
                && Regex.IsMatch(rate.PoeName ?? string.Empty, @"\([A-Z]{3}\)")));

    private static decimal? CalculateLegacyMiamiBillableCft(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl) return null;
        var pol = (rate.PolCode ?? string.Empty).Trim().ToUpperInvariant();
        if (pol is not ("MIA" or "USMIA")
            && !rate.PolName.Contains("Miami", StringComparison.OrdinalIgnoreCase)) return null;
        var hasMatrixFreight = rate.RateDetails.Any(detail =>
            detail.CostDetailType == CostDetailType.Freight
            && detail.ChargeBasis == ChargeBasis.PerChargeableCft
            && (detail.Name.Contains("Flete Miami", StringComparison.OrdinalIgnoreCase)
                || detail.Notes?.Contains("Plan Miami:", StringComparison.OrdinalIgnoreCase) == true));
        if (!hasMatrixFreight) return null;

        var volume = Math.Max(rate.TotalVolumeCbm, 0m);
        if (!string.IsNullOrWhiteSpace(rate.CargoLinesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rate.CargoLinesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    decimal total = 0m;
                    foreach (var cargo in doc.RootElement.EnumerateArray())
                    {
                        if (cargo.ValueKind != JsonValueKind.Object) continue;
                        var billable = ReadDecimal(cargo, "billableVolumeCbm");
                        var physical = ReadDecimal(cargo, "volumeCbm");
                        total += Math.Max(billable > 0m ? billable : physical, 0m);
                    }
                    if (total > 0m) volume = total;
                }
            }
            catch (JsonException) { /* Keep persisted header volume for historical JSON. */ }
        }

        var volumeCft = volume * CftPerCbm;
        var little = rate.RateDetails.Any(detail =>
            detail.Notes?.Contains("Plan Miami: LITTLE", StringComparison.OrdinalIgnoreCase) == true);
        var billableCft = little ? volumeCft
            : Math.Max(volumeCft, Math.Max(rate.TotalWeightKg, 0m) / MiamiKgPerCft);
        return billableCft > 0m ? billableCft : null;
    }

    private static decimal ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            && !element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value))
            return 0m;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : 0m;
    }
}
