namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record RatePickupLocationRequest(
    string Address,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? CargoCondition = null
);
