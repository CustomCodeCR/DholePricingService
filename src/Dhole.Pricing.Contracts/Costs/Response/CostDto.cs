namespace Dhole.Pricing.Contracts.Costs.Response;

public sealed record CostDto(
    Guid Id,
    string Name,
    string CostType,
    string CostDetailType,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
    Guid? AgentId,
    string? AgentName,
    string? AgentCode,
    Guid? PortId,
    string? PortName,
    string? PortCode,
    string? PortRole,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    decimal UtilityAmount,
    string Notes,
    bool IsAccountant,
    bool IsActive
);
