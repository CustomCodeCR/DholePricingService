namespace Dhole.Pricing.Application.Features.Rates;

public sealed record RateCargoLineCommandItem(
    string? Description,
    int Packages,
    int Pallets,
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm
);
