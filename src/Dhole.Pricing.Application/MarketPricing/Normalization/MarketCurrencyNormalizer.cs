using Dhole.Pricing.Application.Abstractions.MarketPricing;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal sealed class MarketCurrencyNormalizer(
    IMarketCurrencyExchangeRateProvider exchangeRateProvider
)
{
    public async Task<MarketCurrencyNormalization> NormalizeAsync(
        string? rawCurrency,
        DateTime referenceDate,
        decimal? explicitRateToUsd = null,
        DateTime? explicitRateDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var original = rawCurrency?.Trim() ?? string.Empty;
        var canonical = MarketTextNormalizer.CanonicalCurrency(rawCurrency);

        if (string.IsNullOrWhiteSpace(canonical))
        {
            return new MarketCurrencyNormalization(
                original,
                string.Empty,
                null,
                null,
                null,
                0m,
                false
            );
        }

        if (canonical == "USD")
        {
            return new MarketCurrencyNormalization(
                original,
                "USD",
                1m,
                NormalizeUtc(explicitRateDate ?? referenceDate),
                "Identity",
                1m,
                true
            );
        }

        if (explicitRateToUsd.HasValue && explicitRateToUsd.Value > 0m)
        {
            return new MarketCurrencyNormalization(
                original,
                "USD",
                explicitRateToUsd.Value,
                NormalizeUtc(explicitRateDate ?? referenceDate),
                "Explicit market observation rate",
                1m,
                true
            );
        }

        var quote = await exchangeRateProvider.GetRateToUsdAsync(
            canonical,
            referenceDate,
            cancellationToken
        );

        if (quote is null || quote.Rate <= 0m)
        {
            return new MarketCurrencyNormalization(
                original,
                canonical,
                null,
                null,
                null,
                0.45m,
                false
            );
        }

        return new MarketCurrencyNormalization(
            original,
            quote.TargetCurrency,
            quote.Rate,
            NormalizeUtc(quote.RateDate),
            quote.Source,
            0.95m,
            true
        );
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
