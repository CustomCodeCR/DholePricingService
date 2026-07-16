using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Features.Costs.Update;

public sealed record UpdateCostCommand(
    Guid Id,
    string Name,
    CostType CostType,
    CostDetailType CostDetailType,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
    Guid? AgentId,
    string? AgentName,
    string? AgentCode,
    Guid? PortId,
    string? PortName,
    string? PortCode,
    CostPortRole? PortRole,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes,
    bool IsAccountant,
    Guid? UpdatedBy
) : ICommand<Result>;
