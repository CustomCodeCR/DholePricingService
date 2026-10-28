namespace Dhole.Pricing.Application.Abstractions.Services;

public sealed record PricingExchangeRateSnapshot(
    decimal Purchase,
    decimal Sale,
    DateTime RateDate,
    DateTime CapturedAtUtc,
    string Source
);

// La propiedad dolares de Hacienda expresa USD por cada EUR (no CRC por EUR).
public sealed record PricingEuroExchangeRateSnapshot(
    decimal DollarsPerEuro,
    decimal ColonesPerEuro,
    DateTime RateDate,
    DateTime CapturedAtUtc,
    string Source
);

public interface IPricingExchangeRateProvider
{
    Task<PricingExchangeRateSnapshot?> GetUsdCrcAsync(CancellationToken cancellationToken = default);
    Task<PricingEuroExchangeRateSnapshot?> GetEurUsdAsync(CancellationToken cancellationToken = default);
}
