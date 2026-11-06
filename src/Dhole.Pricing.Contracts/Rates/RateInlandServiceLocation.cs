namespace Dhole.Pricing.Contracts.Rates;

/// <summary>Optional map coordinates; the written address is authoritative.</summary>
public sealed record RateInlandServiceLocation(
    string Address,
    decimal? Latitude = null,
    decimal? Longitude = null
);
