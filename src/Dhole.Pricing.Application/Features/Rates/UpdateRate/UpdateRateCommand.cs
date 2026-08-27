using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.UpdateRate;

public sealed record UpsertRateExtraDetailCommandItem(
    Guid? Id,
    Guid? CostId,
    string Name,
    CostDetailType CostDetailType,
    CostType CostType,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal CostAmount,
    decimal SaleAmount,
    string? Notes,
    decimal? Quantity,
    ChargeBasis? ChargeBasis
);

public sealed record UpdateRateContainerCommandItem(
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    int Quantity
);

public sealed record UpdateRateCommand(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string AgentCode,
    Guid CarrierId,
    string CarrierName,
    string CarrierCode,
    Guid PolId,
    string PolName,
    string PolCode,
    Guid PoeId,
    string PoeName,
    string PoeCode,
    Guid? PodId,
    string? PodName,
    string? PodCode,
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    Guid? IncotermId,
    string? IncotermName,
    string? IncotermCode,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    int FreeDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    int ContainerQuantity,
    string? ClientName,
    string? IdtraNumber,
    string? QuoNumber,
    string? Includes,
    string? SubjectTo,
    string? Excludes,
    string? TransitTime,
    IReadOnlyCollection<UpsertRateExtraDetailCommandItem> ExtraDetails,
    IReadOnlyCollection<Guid> RemovedExtraDetailIds,
    IReadOnlyCollection<UpdateRateContainerCommandItem> Containers,
    RateType RateType,
    ShipmentMode ShipmentMode,
    decimal KgPerCbm,
    int TotalPackages,
    int TotalPallets,
    decimal TotalWeightKg,
    decimal TotalVolumeCbm,
    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,
    bool CanApproveLowMargin,
    Guid? UpdatedBy
) : ICommand<Result>;
