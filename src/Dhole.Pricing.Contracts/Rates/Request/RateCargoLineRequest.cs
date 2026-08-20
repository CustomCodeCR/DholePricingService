namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record RateCargoLineRequest(
    string? Description,
    int Packages,
    int Pallets,
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm
);
