using Dhole.Pricing.Domain.MarketPricing.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal sealed class MarketChargeNormalizer(MarketCurrencyNormalizer currencyNormalizer)
{
    public async Task<(IReadOnlyCollection<ConvertedMarketCharge> Charges, IReadOnlyCollection<MarketNormalizationIssue> Issues)>
        NormalizeAsync(
            MarketRateNormalizationRequest request,
            MarketCurrencyNormalization baseCurrency,
            CancellationToken cancellationToken = default
        )
    {
        var issues = new List<MarketNormalizationIssue>();
        var rawCharges = BuildRawCharges(request);
        var converted = new List<ConvertedMarketCharge>(rawCharges.Count);
        var referenceDate = NormalizeUtc(request.ReferenceDate ?? DateTime.UtcNow);

        foreach (var charge in rawCharges)
        {
            if (charge.Amount < 0m)
            {
                issues.Add(
                    new MarketNormalizationIssue(
                        "negative_charge",
                        $"El cargo '{charge.Name}' tiene un monto negativo y no se normalizó.",
                        true
                    )
                );
                continue;
            }

            var rawCurrency = string.IsNullOrWhiteSpace(charge.Currency)
                ? request.Currency
                : charge.Currency!;

            MarketCurrencyNormalization currency;
            var sameAsBase =
                MarketTextNormalizer.CanonicalCurrency(rawCurrency)
                == MarketTextNormalizer.CanonicalCurrency(request.Currency);

            if (sameAsBase && baseCurrency.Converted && baseCurrency.RateToUsd.HasValue)
            {
                currency = baseCurrency;
            }
            else
            {
                currency = await currencyNormalizer.NormalizeAsync(
                    rawCurrency,
                    referenceDate,
                    cancellationToken: cancellationToken
                );
            }

            if (!currency.Converted || !currency.RateToUsd.HasValue)
            {
                issues.Add(
                    new MarketNormalizationIssue(
                        "currency_conversion_unavailable",
                        $"No existe conversión determinística a USD para '{rawCurrency}' en el cargo '{charge.Name}'.",
                        true
                    )
                );
                continue;
            }

            var basis = ParseBasis(charge.RawBasis, request.RateBasisHint, request.RawRateBasis);
            var category = charge.CategoryHint ?? ClassifyCategory(charge.Name);

            converted.Add(
                new ConvertedMarketCharge(
                    charge.Name.Trim(),
                    category,
                    basis,
                    charge.Amount,
                    rawCurrency.Trim().ToUpperInvariant(),
                    decimal.Round(
                        charge.Amount * currency.RateToUsd.Value,
                        6,
                        MidpointRounding.AwayFromZero
                    ),
                    currency.RateToUsd.Value,
                    charge.IsAllInTotal
                )
            );
        }

        return (converted, issues);
    }

    private static List<RawMarketCharge> BuildRawCharges(MarketRateNormalizationRequest request)
    {
        var charges = request.Charges?.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList() ?? [];

        if (charges.Count == 0)
        {
            AddAggregate(charges, "Ocean Freight", request.OceanFreight, MarketChargeCategory.OceanFreight, request);
            AddAggregate(charges, "Origin Charges", request.OriginCharges, MarketChargeCategory.OriginCharges, request);
            AddAggregate(charges, "Destination Charges", request.DestinationCharges, MarketChargeCategory.DestinationCharges, request);
            AddAggregate(charges, "Inland Charges", request.InlandCharges, MarketChargeCategory.InlandCharges, request);
            AddAggregate(charges, "Other Charges", request.OtherCharges, MarketChargeCategory.OtherCharges, request);
        }

        if (request.TotalAmount.HasValue)
        {
            charges.Add(
                new RawMarketCharge(
                    "ALL IN TOTAL",
                    request.TotalAmount.Value,
                    request.Currency,
                    request.RawRateBasis,
                    MarketChargeCategory.OtherCharges,
                    true
                )
            );
        }

        return charges;
    }

    private static void AddAggregate(
        ICollection<RawMarketCharge> charges,
        string name,
        decimal? amount,
        MarketChargeCategory category,
        MarketRateNormalizationRequest request
    )
    {
        if (!amount.HasValue)
        {
            return;
        }

        charges.Add(
            new RawMarketCharge(
                name,
                amount.Value,
                request.Currency,
                request.RawRateBasis,
                category
            )
        );
    }

    internal static MarketRateBasis ParseBasis(
        string? rawBasis,
        MarketRateBasis? hint = null,
        string? fallbackRawBasis = null
    )
    {
        var source = string.IsNullOrWhiteSpace(rawBasis) ? fallbackRawBasis : rawBasis;
        var value = MarketTextNormalizer.Normalize(source);
        var compact = MarketTextNormalizer.Compact(value);

        if (string.IsNullOrEmpty(compact))
        {
            return hint.HasValue ? hint.Value : MarketRateBasis.Unknown;
        }

        if (compact.Contains("PERCONTAINER", StringComparison.Ordinal)
            || compact.Contains("PORCONTENEDOR", StringComparison.Ordinal)
            || compact is "CONTAINER" or "CONTENEDOR" or "CNTR")
            return MarketRateBasis.PerContainer;

        if (compact.Contains("PERSHIPMENT", StringComparison.Ordinal)
            || compact.Contains("POREMBARQUE", StringComparison.Ordinal)
            || compact is "SHIPMENT" or "EMBARQUE")
            return MarketRateBasis.PerShipment;

        if (compact.Contains("PERTEU", StringComparison.Ordinal) || compact == "TEU")
            return MarketRateBasis.PerTeu;

        if (compact.Contains("PER100KG", StringComparison.Ordinal)
            || compact.Contains("100KG", StringComparison.Ordinal))
            return MarketRateBasis.Per100Kg;

        if (compact.Contains("PERKG", StringComparison.Ordinal) || compact == "KG")
            return MarketRateBasis.PerKg;

        if (compact.Contains("PERCBM", StringComparison.Ordinal)
            || compact.Contains("CBM", StringComparison.Ordinal)
            || compact.Contains("M3", StringComparison.Ordinal))
            return MarketRateBasis.PerCbm;

        if (compact is "WM" or "W/M" || value.Contains("WEIGHT OR MEASURE", StringComparison.Ordinal))
            return MarketRateBasis.WeightOrMeasure;

        if (compact.Contains("PERHBL", StringComparison.Ordinal) || compact == "HBL")
            return MarketRateBasis.PerHbl;

        if (compact.Contains("PERTRUCK", StringComparison.Ordinal)
            || compact.Contains("PORCAMION", StringComparison.Ordinal)
            || compact is "TRUCK" or "CAMION")
            return MarketRateBasis.PerTruck;

        if (compact.Contains("PERTON", StringComparison.Ordinal)
            || compact.Contains("TONELADA", StringComparison.Ordinal)
            || compact is "TON" or "MT")
            return MarketRateBasis.PerTon;

        if (compact.Contains("MINIMUM", StringComparison.Ordinal)
            || compact.Contains("MINIMO", StringComparison.Ordinal))
            return MarketRateBasis.Minimum;

        return hint.HasValue && hint.Value != MarketRateBasis.Unknown
            ? hint.Value
            : MarketRateBasis.Unknown;
    }

    internal static MarketChargeCategory ClassifyCategory(string? rawName)
    {
        var value = MarketTextNormalizer.Normalize(rawName);
        var compact = MarketTextNormalizer.Compact(rawName);

        if (
            value.Contains("OCEAN FREIGHT", StringComparison.Ordinal)
            || value.Contains("SEA FREIGHT", StringComparison.Ordinal)
            || value.Contains("FLETE INTERNACIONAL", StringComparison.Ordinal)
            || compact is "FREIGHT" or "BASICFREIGHT" or "BASICOCEANFREIGHT"
        )
        {
            return MarketChargeCategory.OceanFreight;
        }

        if (
            value.Contains("ORIGIN", StringComparison.Ordinal)
            || value.Contains("ORIGEN", StringComparison.Ordinal)
            || value.Contains("PICK UP", StringComparison.Ordinal)
            || value.Contains("PICKUP", StringComparison.Ordinal)
            || value.Contains("RECOLECT", StringComparison.Ordinal)
        )
        {
            return MarketChargeCategory.OriginCharges;
        }

        if (
            value.Contains("DESTINATION", StringComparison.Ordinal)
            || value.Contains("DESTINO", StringComparison.Ordinal)
            || value.Contains("DTHC", StringComparison.Ordinal)
            || value.Contains("THC DEST", StringComparison.Ordinal)
            || value.Contains("CARGOS DE NAVIERA", StringComparison.Ordinal)
            || value.Contains("LOCAL CHARGES", StringComparison.Ordinal)
        )
        {
            return MarketChargeCategory.DestinationCharges;
        }

        if (
            value.Contains("INLAND", StringComparison.Ordinal)
            || value.Contains("TRASLADO", StringComparison.Ordinal)
            || value.Contains("TRUCKING", StringComparison.Ordinal)
            || value.Contains("DRAYAGE", StringComparison.Ordinal)
            || value.Contains("HAULAGE", StringComparison.Ordinal)
            || value.Contains("TRANSPORTE INTERNO", StringComparison.Ordinal)
        )
        {
            return MarketChargeCategory.InlandCharges;
        }

        return MarketChargeCategory.OtherCharges;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
