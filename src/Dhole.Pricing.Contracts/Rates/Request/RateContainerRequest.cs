namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record RateContainerRequest(
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    int Quantity
);
