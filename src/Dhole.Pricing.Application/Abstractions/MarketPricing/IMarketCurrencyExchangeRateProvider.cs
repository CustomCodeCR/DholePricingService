namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public sealed record MarketExchangeRateQuote(
    string SourceCurrency,
    string TargetCurrency,
    decimal Rate,
    DateTime RateDate,
    string Source
);

public interface IMarketCurrencyExchangeRateProvider
{
    Task<MarketExchangeRateQuote?> GetRateToUsdAsync(
        string sourceCurrency,
        DateTime referenceDate,
        CancellationToken cancellationToken = default
    );
}
