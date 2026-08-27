using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.CreateRate;

public sealed record CreateRateDetailCommandItem(
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

public sealed record RateContainerCommandItem(
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    int Quantity
);

public sealed record CreateRateCommand(
    Guid? SourceImportFclRateId,
    Guid? AgentId,
    string? AgentName,
    string? AgentCode,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
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
    IReadOnlyCollection<CreateRateDetailCommandItem> Details,
    IReadOnlyCollection<RateContainerCommandItem> Containers,
    RateType RateType,
    ShipmentMode ShipmentMode,
    decimal KgPerCbm,
    int TotalPackages,
    int TotalPallets,
    decimal TotalWeightKg,
    decimal TotalVolumeCbm,
    IReadOnlyCollection<RateCargoLineCommandItem> CargoLines,
    bool CanApproveImportedRate,
    bool CanApproveLowMargin,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
