using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Services;
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
        // Los Id de catálogo son la única entrada confiable. Todos los snapshots usados por
        // Pricing se reconstruyen desde Dhole.Config antes de crear la tarifa.
        try
        {
            var agent = await configCatalog.GetActiveInGroupAsync(
                command.AgentId,
                PricingConstants.CatalogSlugs.Agents,
                cancellationToken
            );
            if (command.AgentId.HasValue && command.AgentId.Value != Guid.Empty && agent is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El agente", PricingConstants.CatalogSlugs.Agents));

            var carrier = await configCatalog.GetActiveInGroupAsync(
                command.CarrierId,
                PricingConstants.CatalogSlugs.Carriers,
                cancellationToken
            );
            if (command.CarrierId.HasValue && command.CarrierId.Value != Guid.Empty && carrier is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "La naviera", PricingConstants.CatalogSlugs.Carriers));

            var pol = await configCatalog.GetActiveInGroupAsync(
                command.PolId, PricingConstants.CatalogSlugs.Pol, cancellationToken);
            if (pol is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POL", PricingConstants.CatalogSlugs.Pol));

            var poe = await configCatalog.GetActiveInGroupAsync(
                command.PoeId, PricingConstants.CatalogSlugs.Poe, cancellationToken);
            if (poe is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POE", PricingConstants.CatalogSlugs.Poe));

            var pod = await configCatalog.GetActiveInGroupAsync(
                command.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken);
            if (pod is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POD", PricingConstants.CatalogSlugs.Pod));

            var currency = await configCatalog.GetActiveInGroupAsync(
                command.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);
            if (currency is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "La moneda", PricingConstants.CatalogSlugs.Currencies));

            PricingConfigCatalogItem? incoterm = null;
            if (command.IncotermId.HasValue && command.IncotermId.Value != Guid.Empty)
            {
                incoterm = await configCatalog.GetActiveInGroupAsync(
                    command.IncotermId, PricingConstants.CatalogSlugs.Incoterms, cancellationToken);
                if (incoterm is null)
                    return Result.Failure<Guid>(PricingErrors.RateInvalidIncoterm);
            }

            var normalizedContainers = new List<RateContainerCommandItem>();
            var requestedContainers = command.Containers.Count > 0
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

            var equipmentCatalogSlug = command.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl
                ? PricingConstants.CatalogSlugs.LandEquipmentTypes
                : PricingConstants.CatalogSlugs.ContainerTypes;
            var equipmentCatalogLabel = command.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl
                ? "El tipo de unidad terrestre"
                : "El tipo de contenedor";

            foreach (var requested in requestedContainers)
            {
                var containerType = await configCatalog.GetActiveInGroupAsync(
                    requested.ContainerTypeId,
                    equipmentCatalogSlug,
                    cancellationToken
                );
                if (containerType is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        equipmentCatalogLabel, equipmentCatalogSlug));

                normalizedContainers.Add(new RateContainerCommandItem(
                    containerType.Id, containerType.SnapshotName(), containerType.Code, requested.Quantity));
            }

            var primaryContainer = normalizedContainers[0];
            command = command with
            {
                AgentId = agent?.Id,
                AgentName = agent?.SnapshotName(),
                AgentCode = agent?.Code,
                CarrierId = carrier?.Id,
                CarrierName = carrier?.SnapshotName(),
                CarrierCode = carrier?.Code,
                PolId = pol.Id,
                PolName = pol.SnapshotName(),
                PolCode = pol.Code,
                PoeId = poe.Id,
                PoeName = poe.SnapshotName(),
                PoeCode = poe.Code,
                PodId = pod.Id,
                PodName = pod.SnapshotName(),
                PodCode = pod.Code,
                ContainerTypeId = primaryContainer.ContainerTypeId,
                ContainerTypeName = primaryContainer.ContainerTypeName,
                ContainerTypeCode = primaryContainer.ContainerTypeCode,
                ContainerQuantity = normalizedContainers.Sum(x => x.Quantity),
                Containers = normalizedContainers,
                IncotermId = incoterm?.Id,
                IncotermName = incoterm?.SnapshotName(preferValue: true),
                IncotermCode = incoterm?.Code,
                CurrencyId = currency.Id,
                CurrencyName = currency.SnapshotName(),
                CurrencyCode = currency.Code,
            };
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.ConfigServiceUnavailable);
        }

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

            // Una tarifa importada aprobada puede seleccionarse antes de que inicie su ventana
            // comercial. CreateFromImportedRate conserva ValidFrom/ValidTo de la tarifa fuente,
            // por lo que no debemos exigir que sea efectiva "hoy"; solo impedir usar una vencida.
            if (importedRate.Status == ImportStatus.Expired)
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
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("Config.", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Config devolvió", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("de Config", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Result.Failure<Guid>(PricingErrors.ConfigServiceUnavailable);
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
