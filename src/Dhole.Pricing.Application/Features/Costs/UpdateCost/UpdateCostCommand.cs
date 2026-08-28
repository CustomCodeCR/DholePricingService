using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

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
    Guid? PolId,
    string? PolName,
    string? PolCode,
    Guid? PoeId,
    string? PoeName,
    string? PoeCode,
    Guid? PodId,
    string? PodName,
    string? PodCode,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes,
    bool IsAccountant,
    IReadOnlyCollection<CostIncotermSelection> Incoterms,
    IReadOnlyCollection<CostServiceSelection> Services,
    ShipmentMode? ShipmentMode,
    ChargeBasis ChargeBasis,
    decimal? MinimumCostAmount,
    decimal? MinimumSaleAmount,
    decimal? KgPerCbm,
    Guid? UpdatedBy
) : ICommand<Result>;
