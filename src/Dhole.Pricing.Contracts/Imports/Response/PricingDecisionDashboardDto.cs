namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record PricingDecisionDashboardDto(
    DateTime? DateFrom,
    DateTime? DateTo,
    decimal MultimodalInternationalLandFreight,
    int TotalOptions,
    IReadOnlyCollection<PricingDecisionLaneDto> Lanes
);

public sealed record PricingDecisionLaneDto(
    string Key,
    string Name,
    string Description,
    int TotalOptions,
    IReadOnlyCollection<PricingDecisionRateDto> Rates
);

public sealed record PricingDecisionRateDto(
    Guid ImportRateId,
    Guid ImportBatchId,
    string Carrier,
    decimal InternationalOceanFreight,
    decimal? InternationalLandFreight,
    string Currency,
    string ContainerType,
    string Pol,
    string Poe,
    DateTime ValidFrom,
    DateTime ValidTo,
    string Status,
    decimal? TotalSale,
    decimal? Margin,
    string SpaceComment,
    decimal SpaceScore,
    string SpaceRisk,
    decimal PriorityScore,
    string PriorityReason
);
