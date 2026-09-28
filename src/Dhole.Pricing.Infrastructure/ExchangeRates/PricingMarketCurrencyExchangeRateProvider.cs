using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Infrastructure.ExchangeRates;

public sealed class PricingMarketCurrencyExchangeRateProvider(
    IPricingExchangeRateProvider exchangeRateProvider
) : IMarketCurrencyExchangeRateProvider
{
    public async Task<MarketExchangeRateQuote?> GetRateToUsdAsync(
        string sourceCurrency,
        DateTime referenceDate,
        CancellationToken cancellationToken = default
    )
    {
        var currency = sourceCurrency.Trim().ToUpperInvariant();

        if (currency == "USD")
        {
            return new MarketExchangeRateQuote(
                "USD",
                "USD",
                1m,
                NormalizeUtc(referenceDate),
                "Identity"
            );
        }

        if (currency != "CRC")
        {
            return null;
        }

        var snapshot = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);
        if (snapshot is null || snapshot.Sale <= 0m)
        {
            return null;
        }

        return new MarketExchangeRateQuote(
            "CRC",
            "USD",
            1m / snapshot.Sale,
            NormalizeUtc(snapshot.RateDate),
            snapshot.Source
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
