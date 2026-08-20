using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.UpdateRate;

public sealed class UpdateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IRateExtraDetailResolver extraDetailResolver,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateRateCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var rate = await rateHeaders.GetByIdWithDetailsAsync(command.Id, cancellationToken);

        if (rate is null || rate.IsDeleted)
        {
            return Result.Failure(PricingErrors.RateHeaderNotFound);
        }

        // Una tarifa cerrada es inmutable porque representa una decisión comercial final.
        // Una tarifa vencida sí se puede editar para renovar su vigencia; al recalcularla
        // se volverá a marcar como vencida si ValidTo continúa en el pasado.
        if (rate.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.Closed)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        if (rate.SourceImportFclRateId.HasValue && command.ShipmentMode != ShipmentMode.Fcl)
            return Result.Failure(PricingErrors.RateImportedStructureLocked);

        var existingDetails = rate.RateDetails.ToDictionary(x => x.Id);

        var extraDetails = command.ExtraDetails ?? Array.Empty<UpsertRateExtraDetailCommandItem>();

        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToHashSet();

        var updatedIds = extraDetails.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();

        if (updatedIds.Distinct().Count() != updatedIds.Length)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        if (updatedIds.Any(removedIds.Contains))
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        foreach (var requestedDetail in extraDetails.Where(x => x.Id.HasValue))
        {
            var id = requestedDetail.Id!.Value;

            if (!existingDetails.TryGetValue(id, out var detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailNotFound);
            }

            if (IsAutomaticFixed(detail) && detail.CostId != requestedDetail.CostId)
            {
                return Result.Failure(PricingErrors.RateCostDetailFixedLocked);
            }
        }

        foreach (var id in removedIds)
        {
            if (!existingDetails.TryGetValue(id, out var detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailNotFound);
            }

            if (IsAutomaticFixed(detail))
            {
                return Result.Failure(PricingErrors.RateCostDetailFixedLocked);
            }
        }

        var resolvedDetails = new List<ResolvedRateExtraDetail>();

        foreach (var detail in extraDetails)
        {
            var resolution = await extraDetailResolver.ResolveAsync(
                new RateExtraDetailInput(
                    detail.Id,
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
                return Result.Failure(resolution.Error!);
            }

            resolvedDetails.Add(resolution.Detail!);
        }

        var normalizedIncoterm = await NormalizeIncotermAsync(
            command.IncotermId,
            configCatalog,
            cancellationToken
        );
        if (command.IncotermId.HasValue && normalizedIncoterm is null)
        {
            return Result.Failure(PricingErrors.RateInvalidIncoterm);
        }
        if (normalizedIncoterm is not null)
        {
            command = command with
            {
                IncotermName = normalizedIncoterm.DisplayValue,
                IncotermCode = normalizedIncoterm.Code,
            };
        }

        var containerSpecs = (command.Containers ?? Array.Empty<UpdateRateContainerCommandItem>())
            .Select(x => new RateContainerAllocationSpec(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity
            ))
            .ToArray();

        if (containerSpecs.Length == 0)
        {
            containerSpecs =
            [
                new RateContainerAllocationSpec(
                    command.ContainerTypeId,
                    command.ContainerTypeName,
                    command.ContainerTypeCode,
                    command.ContainerQuantity
                )
            ];
        }

        if (
            rate.SourceImportFclRateId.HasValue
            && (
                containerSpecs.Length != 1
                || containerSpecs[0].ContainerTypeId != rate.ContainerTypeId
            )
        )
        {
            return Result.Failure(PricingErrors.RateImportedStructureLocked);
        }

        if (!HasValidFreightDistribution(rate, command.ShipmentMode, extraDetails, containerSpecs))
        {
            return Result.Failure(PricingErrors.RateInvalidFreightDistribution);
        }

        var primaryContainer = containerSpecs[0];
        var requestedContainerQuantity = containerSpecs.Sum(x => x.Quantity);
        var currentContainerSignature = rate.RateContainers.Count > 0
            ? rate.RateContainers
                .OrderBy(x => x.ContainerTypeId)
                .Select(x => $"{x.ContainerTypeId:N}:{x.Quantity}")
                .ToArray()
            : [$"{rate.ContainerTypeId:N}:{rate.ContainerQuantity}"];
        var requestedContainerSignature = containerSpecs
            .OrderBy(x => x.ContainerTypeId)
            .Select(x => $"{x.ContainerTypeId:N}:{x.Quantity}")
            .ToArray();
        var containersChanged = !currentContainerSignature.SequenceEqual(requestedContainerSignature);

        var agentId = command.AgentId;
        var agentName = command.AgentName;
        var agentCode = command.AgentCode;
        var carrierId = command.CarrierId;
        var carrierName = command.CarrierName;
        var carrierCode = command.CarrierCode;
        var polId = command.PolId;
        var polName = command.PolName;
        var polCode = command.PolCode;
        var poeId = command.PoeId;
        var poeName = command.PoeName;
        var poeCode = command.PoeCode;
        var podId = command.PodId;
        var podName = command.PodName;
        var podCode = command.PodCode;
        var containerTypeId = primaryContainer.ContainerTypeId;
        var containerTypeName = primaryContainer.ContainerTypeName;
        var containerTypeCode = primaryContainer.ContainerTypeCode;
        var incotermId = command.IncotermId;
        var incotermName = command.IncotermName;
        var incotermCode = command.IncotermCode;
        var currencyId = command.CurrencyId;
        var currencyName = command.CurrencyName;
        var currencyCode = command.CurrencyCode;
        var freeDays = command.FreeDays;
        var validFrom = command.ValidFrom;
        var validTo = command.ValidTo;
        var containerQuantity = requestedContainerQuantity;
        var transitTime = command.TransitTime;

        var selectorsChanged =
            rate.AgentId != agentId
            || rate.CarrierId != carrierId
            || rate.PolId != polId
            || rate.PoeId != poeId
            || rate.PodId != podId
            || containersChanged
            || rate.IncotermId != incotermId
            || rate.CurrencyId != currencyId
            || rate.ShipmentMode != command.ShipmentMode;

        var headerBefore = PricingAuditSnapshots.From(rate);

        var detailBefore = updatedIds
            .Concat(removedIds)
            .Distinct()
            .ToDictionary(id => id, id => PricingAuditSnapshots.From(existingDetails[id]));

        var addedDetails = new List<RateDetail>();
        var modifiedDetails = new List<RateDetail>();
        var automaticallyApprovedLowMargin = false;
        object? automaticApprovalBefore = null;

        try
        {
            rate.Update(
                agentId,
                agentName,
                agentCode,
                carrierId,
                carrierName,
                carrierCode,
                polId,
                polName,
                polCode,
                poeId,
                poeName,
                poeCode,
                podId,
                podName,
                podCode,
                containerTypeId,
                containerTypeName,
                containerTypeCode,
                incotermId,
                incotermName,
                incotermCode,
                currencyId,
                currencyName,
                currencyCode,
                freeDays,
                validFrom,
                validTo,
                containerQuantity,
                command.ClientName,
                command.IdtraNumber,
                command.QuoNumber,
                command.Includes,
                command.SubjectTo,
                command.Excludes,
                transitTime,
                command.RateType,
                command.UpdatedBy
            );

            rate.ReplaceContainerAllocations(containerSpecs, command.UpdatedBy);

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
                command.UpdatedBy
            );

            foreach (var id in removedIds)
            {
                rate.RemoveRateDetail(id, command.UpdatedBy);
            }

            foreach (var detail in resolvedDetails)
            {
                if (detail.Id.HasValue)
                {
                    var chargeBasis = detail.ChargeBasis ?? DefaultChargeBasis(command.ShipmentMode, detail.CostDetailType);
                    rate.UpdateRateDetail(
                        detail.Id.Value,
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
                        detail.Quantity ?? 1m,
                        command.UpdatedBy
                    );

                    modifiedDetails.Add(rate.RateDetails.First(x => x.Id == detail.Id.Value));
                }
                else
                {
                    var chargeBasis = detail.ChargeBasis ?? DefaultChargeBasis(command.ShipmentMode, detail.CostDetailType);
                    var added = rate.AddRateDetail(
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
                        updatedBy: command.UpdatedBy
                    );

                    addedDetails.Add(added);
                }
            }

            // Los costos fijos dependen de naviera, agente, ruta, incoterm y modo de embarque.
            // Primero aplicamos los cambios del payload sobre los detalles existentes y después
            // resincronizamos. De lo contrario SynchronizeAsync elimina los detalles fijos antiguos
            // y el bucle anterior intenta actualizar sus IDs ya eliminados, produciendo
            // "El detalle de la tarifa no existe" al cambiar, por ejemplo, la naviera.
            if (selectorsChanged || command.ShipmentMode is ShipmentMode.Lcl or ShipmentMode.Ltl)
            {
                await fixedCostSynchronizer.SynchronizeAsync(
                    rate,
                    command.UpdatedBy,
                    cancellationToken
                );

                // La resincronización reemplaza detalles fijos automáticos por nuevas instancias.
                // No publiquemos auditorías de Added/Updated para IDs que ya no forman parte
                // de la tarifa después de la sincronización.
                var liveDetailIds = rate.RateDetails.Select(x => x.Id).ToHashSet();
                addedDetails.RemoveAll(x => !liveDetailIds.Contains(x.Id));
                modifiedDetails.RemoveAll(x => !liveDetailIds.Contains(x.Id));
            }

            rate.SetAmounts(command.UpdatedBy);

            if (rate.RequiredApproval && command.CanApproveLowMargin)
            {
                automaticApprovalBefore = PricingAuditSnapshots.From(rate);
                rate.SetApprovalMargin(
                    command.UpdatedBy,
                    isApproved: true,
                    openAfterAutomaticApproval: true
                );
                automaticallyApprovedLowMargin = true;
            }

            // Permite renovar una tarifa Expired. Si la nueva vigencia sigue vencida,
            // conserva Expired en vez de reabrirla accidentalmente por SetAmounts().
            rate.MarkExpired(DateTime.UtcNow, command.UpdatedBy);
        }
        catch (InvalidOperationException exception)
        {
            // Antes cualquier validación de datos terminaba reportándose como un error
            // de estado, ocultando la causa real al frontend.
            return Result.Failure(PricingErrors.RateUpdateValidationFailed(exception.Message));
        }

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: rate.Id,
                ActorUserId: command.UpdatedBy,
                Before: headerBefore,
                After: PricingAuditSnapshots.From(rate),
                Payload: new
                {
                    SelectorsChanged = selectorsChanged,
                    AddedDetailIds = addedDetails.Select(x => x.Id).ToArray(),
                    UpdatedDetailIds = modifiedDetails.Select(x => x.Id).ToArray(),
                    RemovedDetailIds = removedIds.ToArray(),
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

        if (automaticallyApprovedLowMargin)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderApprovalChanged,
                    Action: PricingAuditActions.Approved,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: rate.Id,
                    ActorUserId: command.UpdatedBy,
                    Before: automaticApprovalBefore,
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

        foreach (var detail in addedDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailAdded,
                    Action: PricingAuditActions.Added,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.UpdatedBy,
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

        foreach (var detail in modifiedDetails)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailUpdated,
                    Action: PricingAuditActions.Updated,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: detail.Id,
                    ActorUserId: command.UpdatedBy,
                    Before: detailBefore[detail.Id],
                    After: PricingAuditSnapshots.From(detail),
                    Payload: new
                    {
                        RateHeaderId = rate.Id,
                        RateDetailId = detail.Id,
                        detail.CostId,
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.UtilityAmount,
                    }
                ),
                cancellationToken
            );
        }

        foreach (var removedId in removedIds)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateDetailRemoved,
                    Action: PricingAuditActions.Removed,
                    EntityType: PricingAuditEntityTypes.RateDetail,
                    EntityId: removedId,
                    ActorUserId: command.UpdatedBy,
                    Before: detailBefore[removedId],
                    Payload: new { RateHeaderId = rate.Id, RateDetailId = removedId }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);

        return Result.Success();
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

    private static bool HasValidFreightDistribution(
        RateHeader rate,
        ShipmentMode shipmentMode,
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> details,
        IReadOnlyCollection<RateContainerAllocationSpec> containers
    )
    {
        if (rate.SourceImportFclRateId.HasValue) return true;
        if (shipmentMode != ShipmentMode.Fcl) return true;

        var freight = details.Where(x => x.CostDetailType == CostDetailType.Freight).ToArray();
        if (freight.Length != containers.Count) return false;
        if (
            containers.Count == 1
            && freight.Length == 1
            && (!freight[0].Quantity.HasValue || freight[0].Quantity.Value == 0m)
        )
            return true;
        if (freight.Any(x => !x.Quantity.HasValue || x.Quantity.Value <= 0)) return false;

        return freight.Sum(x => x.Quantity!.Value) == containers.Sum(x => x.Quantity);
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

    private static bool IsAutomaticFixed(RateDetail detail)
    {
        return detail.CostId.HasValue && detail.CostType == CostType.Fixed;
    }
}
