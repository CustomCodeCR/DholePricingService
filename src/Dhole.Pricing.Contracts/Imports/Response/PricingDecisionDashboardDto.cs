namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record PricingDecisionDashboardDto(
    DateTime? DateFrom,
    DateTime? DateTo,
    int TotalOptions,
    IReadOnlyCollection<PricingDecisionLaneDto> Lanes
);

public sealed record PricingDecisionLaneDto(
    string Key,
    string Name,
    int TotalOptions,
    IReadOnlyCollection<PricingDecisionRateDto> Rates
);

public sealed record PricingDecisionRateDto(
    Guid ImportRateId,
    string Carrier,
    decimal InternationalOceanFreight,
    decimal? InternationalLandFreight,
    string Currency,
    string ContainerType,
    string Pol,
    string Poe,
    DateTime ValidFrom,
    DateTime ValidTo
);
