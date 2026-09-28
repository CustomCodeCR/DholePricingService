namespace Dhole.Pricing.Contracts.Costs.Response;

public sealed record CostRelationDto(
    Guid Id,
    string Name,
    string Code
);
