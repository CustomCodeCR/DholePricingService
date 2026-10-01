namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RatePickupLocationDto(
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    string? CargoCondition
);
