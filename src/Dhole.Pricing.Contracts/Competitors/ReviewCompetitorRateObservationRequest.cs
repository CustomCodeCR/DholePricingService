namespace Dhole.Pricing.Contracts.Competitors;

public sealed record ReviewCompetitorRateObservationRequest(
    Guid IncotermId,
    Guid PolId,
    Guid? PoeId,
    Guid? PodId,
    Guid? CarrierId,
    Guid? ContainerTypeId,
    string Currency,
    decimal OriginalAmount,
    DateTime ValidFrom,
    DateTime ValidTo
);
