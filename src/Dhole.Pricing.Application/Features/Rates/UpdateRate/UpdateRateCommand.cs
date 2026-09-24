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
    Guid? WarehouseId,
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
    public IReadOnlyCollection<Guid>? FinalBackupStorageIds { get; init; }

    // El middleware de edición neutraliza temporalmente SourceImportFclRateId en la entidad
    // para permitir cambiar la fuente/estructura. Conservamos el valor pedido por Web como
    // contexto de sincronización para no perder los cargos fijos explícitos de la importación.
    public Guid? SourceImportFclRateId { get; init; }

    // Motivo ingresado por el usuario antes de editar. Se conserva en auditoría
    // para explicar por qué se realizó cada modificación de la cotización.
    public string? UpdateReason { get; init; }

    // La vigencia seleccionada por Pricing forma parte explícita de la revisión.
    // No la sustituimos por la fecha actual porque al duplicar/renovar una tarifa
    // el usuario puede necesitar una ventana futura definida por la nueva oferta.
    public DateTime ValidFrom { get; } = RequestedValidFrom.Date;
}
