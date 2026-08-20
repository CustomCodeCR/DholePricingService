namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateContainerDto(
    Guid Id,
    Guid RateHeaderId,
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    int Quantity
);
