namespace Dhole.Pricing.Contracts.Competitors;

public sealed record UpsertCompetitorTariffRequest(
    Guid? Id,
    IReadOnlyCollection<Guid> PolIds,
    IReadOnlyCollection<Guid> PoeIds,
    IReadOnlyCollection<Guid> PodIds,
    IReadOnlyCollection<Guid> CarrierIds,
    DateTime ValidFrom,
    DateTime ValidTo,
    string ShipmentMode,
    Guid StorageId
);
