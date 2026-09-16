using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Rates.Entities;

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
    ChargeBasis? ChargeBasis,
    bool ApplyDestinationTax = false,
    decimal DestinationTaxRate = 0m,
    string? BillToClient = null
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
    DateTime RequestedValidFrom,
    DateTime ValidTo,
    int ContainerQuantity,
    string? ClientName,
    string? ExecutiveName,
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
    string? PickupAddress,
    decimal? PickupLatitude,
    decimal? PickupLongitude,
    bool CanApproveLowMargin,
    RateOperationType OperationType,
    IReadOnlyCollection<RateServiceSelection> Services,
    decimal? ExchangeRatePurchase,
    decimal? ExchangeRateSale,
    decimal? ExchangeRateApplied,
    bool UseAllInPresentation,
    Guid? UpdatedBy
) : ICommand<Result>
{
    // La vigencia de una tarifa editada siempre reinicia en la fecha operativa de
    // Costa Rica (UTC-6, sin horario de verano). Conservamos el valor solicitado
    // únicamente para mantener compatible la firma del comando; el handler usa esta
    // fecha autoritativa y nunca una vigencia histórica enviada por el cliente.
    public DateTime ValidFrom { get; } = DateTimeOffset.UtcNow
        .ToOffset(TimeSpan.FromHours(-6))
        .Date;
}
