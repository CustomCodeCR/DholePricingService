namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateCargoLineDto(
    string? Description,
    int Packages,
    int Pallets,
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal VolumeCbm,
    bool? IsStackable = null,
    decimal BillableVolumeCbm = 0m,
    decimal DeadSpaceCbm = 0m
);
