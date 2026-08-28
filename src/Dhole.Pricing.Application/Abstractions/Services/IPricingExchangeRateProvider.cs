namespace Dhole.Pricing.Application.Abstractions.Services;

public sealed record PricingExchangeRateSnapshot(
    decimal Purchase,
    decimal Sale,
    DateTime RateDate,
    DateTime CapturedAtUtc,
    string Source
);

public interface IPricingExchangeRateProvider
{
    Task<PricingExchangeRateSnapshot?> GetUsdCrcAsync(CancellationToken cancellationToken = default);
}
