namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record ReviewImportRateRequest(
    Guid ImportProfileId,
    Guid PolId,
    Guid PoeId,
    Guid PodId,
    Guid CarrierId,
    Guid AgentId,
    Guid ContainerTypeId,
    Guid CurrencyId,
    string? Commodity,
    string? SpaceComment,
    decimal OceanFreight,
    decimal OriginCharges,
    decimal DestinationCharges,
    decimal Surcharges,
    decimal? TotalSale,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? ReviewNotes
);
