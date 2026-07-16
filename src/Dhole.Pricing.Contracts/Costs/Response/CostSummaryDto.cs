namespace Dhole.Pricing.Contracts.Costs.Response;

public sealed record CostSummaryDto(
    Guid Id,
    string Name,
    string CostType,
    string CostDetailType,
    string? CarrierName,
    string? AgentName,
    string? PortName,
    string? PortRole,
    string CurrencyName,
    decimal CostAmount,
    decimal SaleAmount,
    string Notes,
    bool IsAccountant,
    bool IsActive
);
