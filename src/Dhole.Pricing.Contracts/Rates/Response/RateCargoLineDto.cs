namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateCargoLineDto(
    string? Description,
    int Packages,
    int Pallets,
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal VolumeCbm
);
