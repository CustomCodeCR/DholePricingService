using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.CreateRate;

public sealed class CreateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IImportFclRateRepository importedRates,
    IRateCodeGenerator rateCodeGenerator,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IRateExtraDetailResolver extraDetailResolver,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IImportRateCacheService importCache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedDetails = new List<ResolvedRateExtraDetail>();
        var importedFreightOverride = command.SourceImportFclRateId.HasValue
            ? command.Details.FirstOrDefault(x => x.CostDetailType == CostDetailType.Freight)
            : null;

        foreach (
            var detail in command.Details.Where(detail =>
                !command.SourceImportFclRateId.HasValue
                || detail.CostDetailType != CostDetailType.Freight
            )
        )
        {
            var resolution = await extraDetailResolver.ResolveAsync(
                new RateExtraDetailInput(
                    Id: null,
                    detail.CostId,
                    detail.Name,
                    detail.CostDetailType,
                    detail.CostType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    detail.Quantity,
                    detail.ChargeBasis
                ),
                cancellationToken
            );

            if (!resolution.IsSuccess)
            {
                return Result.Failure<Guid>(resolution.Error!);
            }

            resolvedDetails.Add(resolution.Detail!);
        }

        ImportFclRates? importedRate = null;
        var automaticallyApprovedImport = false;
        var automaticallyApprovedLowMargin = false;

        if (command.SourceImportFclRateId.HasValue)
        {
            if (command.ShipmentMode != ShipmentMode.Fcl)
                return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);

            var sourceId = command.SourceImportFclRateId.Value;

            importedRate = await importedRates.GetByIdAsync(sourceId, cancellationToken);

            if (importedRate is null || importedRate.IsDeleted)
            {
                return Result.Failure<Guid>(PricingErrors.ImportFclRateNotFound);
            }

            var today = DateTime.UtcNow.Date;
            importedRate.ExpireIfNeeded(today, command.CreatedBy);

            if (!importedRate.IsEffectiveOn(today))
            {
                return Result.Failure<Guid>(PricingErrors.ImportFclRateOutsideValidity);
            }

            if (importedRate.Status == ImportStatus.Pending)
            {
                if (!command.CanApproveImportedRate)
                {
                    return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
                }

                importedRate.Approve(command.CreatedBy);
                automaticallyApprovedImport = true;
            }

            if (importedRate.Status != ImportStatus.Approved)
            {
                return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
            }

            if (
                command.Containers.Count != 1
                || command.Containers.First().ContainerTypeId != importedRate.ContainerTypeId
            )
            {
                return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
            }
        }

        if (!HasValidFreightDistribution(command))
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }

        var normalizedIncoterm = await NormalizeIncotermAsync(
            command.IncotermId,
            configCatalog,
            cancellationToken
        );
        if (command.IncotermId.HasValue && normalizedIncoterm is null)
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }
        if (normalizedIncoterm is not null)
        {
            command = command with
            {
                IncotermName = normalizedIncoterm.DisplayValue,
                IncotermCode = normalizedIncoterm.Code,
            };
        }

        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);

        RateHeader rate;

        try
        {
            rate = importedRate is null
                ? CreateManualRate(command, rateCode)
                : CreateFromImportedRate(command, importedRate, rateCode);

            var cargoProfile = RateCargoProfileFactory.Create(
                command.ShipmentMode,
                command.KgPerCbm,
                command.CargoLines,
                command.TotalPackages,
                command.TotalPallets,
                command.TotalWeightKg,
                command.TotalVolumeCbm
            );
            rate.ConfigureShipment(
                command.ShipmentMode,
                cargoProfile.TotalPackages,
                cargoProfile.TotalPallets,
                cargoProfile.TotalWeightKg,
                cargoProfile.TotalVolumeCbm,
                cargoProfile.KgPerCbm,
                cargoProfile.CargoLinesJson,
                command.CreatedBy
            );

            if (importedRate is not null)
            {
                AddImportedFreight(rate, importedRate, importedFreightOverride, command.CreatedBy);
            }

            foreach (var detail in resolvedDetails)
            {
                var chargeBasis = detail.ChargeBasis ?? DefaultChargeBasis(command.ShipmentMode, detail.CostDetailType);
                rate.AddRateDetail(
                    rate.Id,
                    detail.CostId,
                    detail.Name,
                    detail.CostDetailType,
                    detail.CostType,
                    chargeBasis,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    quantity: detail.Quantity ?? 1m,
                    updatedBy: command.CreatedBy
                );
            }

            await fixedCostSynchronizer.SynchronizeAsync(
                rate,
                command.CreatedBy,
                cancellationToken
            );

            rate.SetAmounts(command.CreatedBy);

            if (rate.RequiredApproval && command.CanApproveLowMargin)
            {
                rate.SetApprovalMargin(
                    command.CreatedBy,
                    isApproved: true,
                    openAfterAutomaticApproval: true
                );
                automaticallyApprovedLowMargin = true;
            }
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }

        await rateHeaders.AddAsync(rate, cancellationToken);

        if (importedRate is not null)
        {
            importedRate.CreatedAsRate(rate.Id, command.CreatedBy);
        }

        if (importedRate is not null && automaticallyApprovedImport)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateApproved,
                    Action: PricingAuditActions.Approved,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importedRate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(importedRate),
                    Payload: new { importedRate.Id, AutomaticallyApproved = true }
                ),
                cancellationToken
            );
        }

        if (automaticallyApprovedLowMargin)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderApprovalChanged,
                    Action: PricingAuditActions.Approved,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: rate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(rate),
                    Payload: new
                    {
                        rate.Id,
                        rate.MarginPercentage,
                        rate.RequiredApproval,
                        Status = rate.Status.ToString(),
                        AutomaticallyApprovedByScope = true,
                    }
                ),
                cancellationToken
            );
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: rate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(rate),
                Payload: new
                {
                    rate.Id,
                    rate.SourceImportFclRateId,
                    rate.TotalCostAmount,
                    rate.TotalSaleAmount,
                    rate.TotalUtilityAmount,
                    rate.MarginPercentage,
                    rate.RequiredApproval,
                    Status = rate.Status.ToString(),
                    AutomaticallyApprovedLowMargin = automaticallyApprovedLowMargin,
                }
            ),
            cancellationToken
        );

        foreach (var detail in rate.RateDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailAdded,
                    Action: PricingAuditActions.Added,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(detail),
                    Payload: new
                    {
                        RateHeaderId = rate.Id,
                        RateDetailId = detail.Id,
                        detail.CostId,
                        CostType = detail.CostType.ToString(),
                        CostDetailType = detail.CostDetailType.ToString(),
                    }
                ),
                cancellationToken
            );
        }

        if (importedRate is not null)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateCreatedAsRate,
                    Action: PricingAuditActions.CreatedAsRate,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importedRate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(importedRate),
                    Payload: new { ImportedRateId = importedRate.Id, RateHeaderId = rate.Id }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        if (importedRate is not null)
        {
            await importCache.RemoveImportRateCacheAsync(
                importedRate.Id,
                importedRate.ImportBatchId,
                cancellationToken
            );
        }

        return Result.Success(rate.Id);
    }

    private static RateHeader CreateManualRate(CreateRateCommand command, string rateCode)
    {
        var containers = command.Containers
            .Select(x => new RateContainerAllocationSpec(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity
            ))
            .ToArray();

        if (containers.Length == 0)
        {
            containers =
            [
                new RateContainerAllocationSpec(
                    command.ContainerTypeId,
                    command.ContainerTypeName,
                    command.ContainerTypeCode,
                    command.ContainerQuantity
                )
            ];
        }

        var primary = containers[0];
        var totalQuantity = containers.Sum(x => x.Quantity);

        var rate = RateHeader.Create(
            rateCode,
            sourceImportFclRateId: null,
            command.AgentId,
            command.AgentName,
            command.AgentCode,
            command.CarrierId,
            command.CarrierName,
            command.CarrierCode,
            command.PolId,
            command.PolName,
            command.PolCode,
            command.PoeId,
            command.PoeName,
            command.PoeCode,
            command.PodId,
            command.PodName,
            command.PodCode,
            primary.ContainerTypeId,
            primary.ContainerTypeName,
            primary.ContainerTypeCode,
            command.IncotermId,
            command.IncotermName,
            command.IncotermCode,
            command.CurrencyId,
            command.CurrencyName,
            command.CurrencyCode,
            command.FreeDays,
            command.ValidFrom,
            command.ValidTo,
            totalQuantity,
            command.ClientName,
            command.IdtraNumber,
            command.QuoNumber,
            command.Includes,
            command.SubjectTo,
            command.Excludes,
            command.TransitTime,
            command.RateType,
            command.CreatedBy
        );

        rate.ReplaceContainerAllocations(containers, command.CreatedBy);
        return rate;
    }

    private static RateHeader CreateFromImportedRate(
        CreateRateCommand command,
        ImportFclRates importedRate,
        string rateCode
    )
    {
        return RateHeader.Create(
            rateCode,
            importedRate.Id,
            command.AgentId,
            command.AgentName,
            command.AgentCode,
            command.CarrierId,
            command.CarrierName,
            command.CarrierCode,
            command.PolId,
            command.PolName,
            command.PolCode,
            command.PoeId,
            command.PoeName,
            command.PoeCode,
            command.PodId,
            command.PodName,
            command.PodCode,
            command.Containers.First().ContainerTypeId,
            command.Containers.First().ContainerTypeName,
            command.Containers.First().ContainerTypeCode,
            command.IncotermId,
            command.IncotermName,
            command.IncotermCode,
            command.CurrencyId,
            command.CurrencyName,
            command.CurrencyCode,
            importedRate.FreeDays,
            importedRate.ValidFrom,
            importedRate.ValidTo,
            command.Containers.Sum(x => x.Quantity),
            command.ClientName,
            command.IdtraNumber,
            command.QuoNumber,
            command.Includes,
            command.SubjectTo,
            command.Excludes,
            command.TransitTime,
            command.RateType,
            command.CreatedBy
        );
    }

    private static async Task<NormalizedIncoterm?> NormalizeIncotermAsync(
        Guid? incotermId,
        IPricingConfigCatalogClient configCatalog,
        CancellationToken cancellationToken
    )
    {
        if (!incotermId.HasValue || incotermId.Value == Guid.Empty) return null;

        var item = await configCatalog.GetActiveByIdAsync(incotermId.Value, cancellationToken);
        if (
            item is null
            || !item.CatalogGroupSlug.Equals("incoterms", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        var displayValue = string.IsNullOrWhiteSpace(item.Value) ? item.Name : item.Value.Trim();
        return new NormalizedIncoterm(displayValue, item.Code);
    }

    private sealed record NormalizedIncoterm(string DisplayValue, string Code);

    private static ChargeBasis DefaultChargeBasis(ShipmentMode shipmentMode, CostDetailType detailType)
    {
        if (detailType == CostDetailType.Documentation)
            return ChargeBasis.PerDocument;

        if (detailType is not (CostDetailType.Freight or CostDetailType.InlandTransport))
            return ChargeBasis.PerShipment;

        return shipmentMode switch
        {
            ShipmentMode.Fcl => ChargeBasis.PerContainer,
            ShipmentMode.Ftl => ChargeBasis.PerTruck,
            ShipmentMode.Lcl or ShipmentMode.Ltl => ChargeBasis.PerChargeableCbm,
            _ => ChargeBasis.PerShipment,
        };
    }

    private static bool HasValidFreightDistribution(CreateRateCommand command)
    {
        if (command.SourceImportFclRateId.HasValue) return true;
        if (command.ShipmentMode != ShipmentMode.Fcl) return true;

        IReadOnlyCollection<RateContainerCommandItem> containers = command.Containers.Count > 0
            ? command.Containers
            : new[]
            {
                new RateContainerCommandItem(
                    command.ContainerTypeId,
                    command.ContainerTypeName,
                    command.ContainerTypeCode,
                    command.ContainerQuantity
                )
            };
        var freight = command.Details.Where(x => x.CostDetailType == CostDetailType.Freight).ToArray();

        if (freight.Length != containers.Count) return false;
        if (containers.Count == 1 && freight.Length == 1 && !freight[0].Quantity.HasValue)
            return true;
        if (freight.Any(x => !x.Quantity.HasValue || x.Quantity.Value <= 0)) return false;

        return freight.Sum(x => x.Quantity!.Value) == containers.Sum(x => x.Quantity);
    }

    private static void AddImportedFreight(
        RateHeader rate,
        ImportFclRates importedRate,
        CreateRateDetailCommandItem? saleOverride,
        Guid? createdBy
    )
    {
        var costAmount = importedRate.OceanFreight ?? importedRate.Freight;
        var saleAmount = saleOverride?.SaleAmount
            ?? importedRate.TotalSale
            ?? importedRate.OceanFreight
            ?? importedRate.Freight;

        rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: "Flete internacional",
            CostDetailType.Freight,
            CostType.Variable,
            ChargeBasis.PerContainer,
            importedRate.CurrencyId,
            importedRate.CurrencyName,
            importedRate.CurrencyCode,
            costAmount,
            saleAmount,
            notes: saleOverride?.Notes,
            quantity: rate.ContainerQuantity,
            updatedBy: createdBy
        );
    }
}
