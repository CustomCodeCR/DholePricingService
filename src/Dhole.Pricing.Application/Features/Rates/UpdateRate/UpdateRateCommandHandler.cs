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
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.UpdateRate;

public sealed class UpdateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateRevisionRepository rateRevisions,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IRateExtraDetailResolver extraDetailResolver,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateRateCommand, Result>
{
    private static readonly Guid OwnLclInternalAgentId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c41");
    private const string OwnLclInternalAgentName = "Grupo Castro Fallas";
    private const string OwnLclInternalAgentCode = "GCF";
    private static readonly Guid LclInternalCarrierId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c44");
    private const string LclInternalCarrierName = "No aplica (LCL)";
    private const string LclInternalCarrierCode = "LCL";
    private static readonly Guid LandInternalAgentId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c42");
    private const string LandInternalAgentName = "No aplica (terrestre)";
    private const string LandInternalAgentCode = "LAND";
    private static readonly Guid LandInternalCarrierId = new("7f4ed7d4-60a3-4f69-90e0-e2e2b24b4c43");
    private const string LandInternalCarrierName = "No aplica (terrestre)";
    private const string LandInternalCarrierCode = "LAND";

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

        if (string.IsNullOrWhiteSpace(command.UpdateReason))
        {
            return Result.Failure(PricingErrors.RateUpdateReasonIsRequired);
        }

        // Una tarifa cerrada es inmutable porque representa una decisión comercial final.
        // Una tarifa vencida sí se puede editar para renovar su vigencia; al recalcularla
        // se volverá a marcar como vencida si ValidTo continúa en el pasado.
        if (rate.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.Closed)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        var acceptedRevision = rate.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.AcceptedByClient
            ? RateRevisionSnapshotFactory.Capture(rate)
            : null;
        var acceptedRevisionNumber = rate.RevisionNumber;

        if (rate.SourceImportFclRateId.HasValue && command.ShipmentMode != ShipmentMode.Fcl)
            return Result.Failure(PricingErrors.RateImportedStructureLocked);

        var ownLclWithoutAgent = command.ShipmentMode == ShipmentMode.Lcl && (
            command.AgentId == OwnLclInternalAgentId
            || rate.AgentId == OwnLclInternalAgentId
            || (
                command.AgentId == Guid.Empty
                && (
                    string.Equals(command.AgentCode, OwnLclInternalAgentCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(command.AgentName, OwnLclInternalAgentName, StringComparison.OrdinalIgnoreCase)
                )
            )
        );
        var lclWithoutCarrier =
            command.ShipmentMode == ShipmentMode.Lcl
            && command.CarrierId == Guid.Empty;
        var landWithoutProvider = command.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl;

        // Rehidratamos todos los selectores desde Config. De esta manera cambiar naviera,
        // agente, ruta, contenedor, moneda o Incoterm nunca persiste Name/Code enviados por Web.
        try
        {
            PricingConfigCatalogItem? agent = null;
            var requestedCatalogAgent = !landWithoutProvider
                && command.AgentId != Guid.Empty
                && command.AgentId != OwnLclInternalAgentId;
            if (requestedCatalogAgent)
            {
                agent = await configCatalog.GetActiveInGroupAsync(
                    command.AgentId, PricingConstants.CatalogSlugs.Agents, cancellationToken);
                if (agent is null)
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        "El agente", PricingConstants.CatalogSlugs.Agents));
            }
            else if (!ownLclWithoutAgent && !landWithoutProvider)
            {
                return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                    "El agente", PricingConstants.CatalogSlugs.Agents));
            }

            PricingConfigCatalogItem? carrier = null;
            if (!landWithoutProvider && command.CarrierId != Guid.Empty)
            {
                carrier = await configCatalog.GetActiveInGroupAsync(
                    command.CarrierId, PricingConstants.CatalogSlugs.Carriers, cancellationToken);
                if (carrier is null)
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        "La naviera", PricingConstants.CatalogSlugs.Carriers));
            }
            else if (!landWithoutProvider && !lclWithoutCarrier)
            {
                return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                    "La naviera", PricingConstants.CatalogSlugs.Carriers));
            }

            var pol = await configCatalog.GetActiveInGroupAsync(
                command.PolId, PricingConstants.CatalogSlugs.Pol, cancellationToken);
            if (pol is null)
                return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                    "El POL", PricingConstants.CatalogSlugs.Pol));

            var poe = await configCatalog.GetActiveInGroupAsync(
                command.PoeId, PricingConstants.CatalogSlugs.Poe, cancellationToken);
            if (poe is null)
                return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                    "El POE", PricingConstants.CatalogSlugs.Poe));

            PricingConfigCatalogItem? pod = null;
            if (command.PodId.HasValue && command.PodId.Value != Guid.Empty)
            {
                pod = await configCatalog.GetActiveInGroupAsync(
                    command.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken);
                if (pod is null)
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        "El POD", PricingConstants.CatalogSlugs.Pod));
            }

            var currency = await configCatalog.GetActiveInGroupAsync(
                command.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);
            if (currency is null)
                return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                    "La moneda", PricingConstants.CatalogSlugs.Currencies));

            PricingConfigCatalogItem? incoterm = null;
            if (command.IncotermId.HasValue && command.IncotermId.Value != Guid.Empty)
            {
                incoterm = await configCatalog.GetActiveInGroupAsync(
                    command.IncotermId, PricingConstants.CatalogSlugs.Incoterms, cancellationToken);
                if (incoterm is null)
                    return Result.Failure(PricingErrors.RateInvalidIncoterm);
            }

            var normalizedServices = new List<RateServiceSelection>();
            foreach (var selected in command.Services ?? Array.Empty<RateServiceSelection>())
            {
                var service = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.PricingServices, cancellationToken);
                if (service is null)
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        "El servicio de Pricing", PricingConstants.CatalogSlugs.PricingServices));
                normalizedServices.Add(new RateServiceSelection(
                    service.Id, service.SnapshotName(preferValue: true), service.Code));
            }

            var normalizedContainers = new List<UpdateRateContainerCommandItem>();
            var requestedContainers = command.Containers is { Count: > 0 }
                ? command.Containers
                : new[]
                {
                    new UpdateRateContainerCommandItem(
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
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        equipmentCatalogLabel, equipmentCatalogSlug));

                normalizedContainers.Add(new UpdateRateContainerCommandItem(
                    containerType.Id, containerType.SnapshotName(), containerType.Code, requested.Quantity));
            }

            var normalizedPrimaryContainer = normalizedContainers[0];
            command = command with
            {
                AgentId = landWithoutProvider
                    ? LandInternalAgentId
                    : agent?.Id ?? OwnLclInternalAgentId,
                AgentName = landWithoutProvider
                    ? LandInternalAgentName
                    : agent?.SnapshotName() ?? OwnLclInternalAgentName,
                AgentCode = landWithoutProvider
                    ? LandInternalAgentCode
                    : agent?.Code ?? OwnLclInternalAgentCode,
                CarrierId = landWithoutProvider
                    ? LandInternalCarrierId
                    : lclWithoutCarrier ? LclInternalCarrierId : carrier!.Id,
                CarrierName = landWithoutProvider
                    ? LandInternalCarrierName
                    : lclWithoutCarrier ? LclInternalCarrierName : carrier!.SnapshotName(),
                CarrierCode = landWithoutProvider
                    ? LandInternalCarrierCode
                    : lclWithoutCarrier ? LclInternalCarrierCode : carrier!.Code,
                PolId = pol.Id,
                PolName = pol.SnapshotName(),
                PolCode = pol.Code,
                PoeId = poe.Id,
                PoeName = poe.SnapshotName(),
                PoeCode = poe.Code,
                PodId = pod?.Id,
                PodName = pod?.SnapshotName(),
                PodCode = pod?.Code,
                ContainerTypeId = normalizedPrimaryContainer.ContainerTypeId,
                ContainerTypeName = normalizedPrimaryContainer.ContainerTypeName,
                ContainerTypeCode = normalizedPrimaryContainer.ContainerTypeCode,
                ContainerQuantity = normalizedContainers.Sum(x => x.Quantity),
                Containers = normalizedContainers,
                IncotermId = incoterm?.Id,
                IncotermName = incoterm?.SnapshotName(preferValue: true),
                IncotermCode = incoterm?.Code,
                CurrencyId = currency.Id,
                CurrencyName = currency.SnapshotName(),
                CurrencyCode = currency.Code,
                Services = normalizedServices,
            };
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(PricingErrors.ConfigServiceUnavailable);
        }

        var existingDetails = rate.RateDetails.ToDictionary(x => x.Id);

        var extraDetails = command.ExtraDetails ?? Array.Empty<UpsertRateExtraDetailCommandItem>();

        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToHashSet();

        // Pantalla 7 is authoritative for persisted rate details during an edit.
        // Keep the original fixed-cost snapshot so the synchronizer can distinguish between
        // details that already belonged to the quote and new catalog costs discovered later.
        var automaticFixedCostIdsBeforeUpdate = existingDetails.Values
            .Where(IsAutomaticFixed)
            .Select(x => x.CostId!.Value)
            .ToHashSet();
        var explicitlyRemovedAutomaticFixedCostIds = removedIds
            .Where(existingDetails.ContainsKey)
            .Select(id => existingDetails[id])
            .Where(IsAutomaticFixed)
            .Select(x => x.CostId!.Value)
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
            if (!existingDetails.ContainsKey(id))
            {
                return Result.Failure(PricingErrors.RateCostDetailNotFound);
            }
        }

        var resolvedDetails = new List<ResolvedRateExtraDetail>();

        foreach (var detail in extraDetails)
        {
            var normalizedDetailType = NormalizeLclDetailType(
                command.ShipmentMode,
                detail.Name,
                detail.CostDetailType
            );
            var normalizedChargeBasis = NormalizeLclChargeBasis(
                command.ShipmentMode,
                detail.Name,
                detail.ChargeBasis
            );
            var normalizedQuantity = NormalizeLclDetailQuantity(
                command.ShipmentMode,
                detail.Name,
                detail.Quantity,
                command.TotalVolumeCbm
            );

            var resolution = await extraDetailResolver.ResolveAsync(
                new RateExtraDetailInput(
                    detail.Id,
                    detail.CostId,
                    detail.Name,
                    normalizedDetailType,
                    detail.CostType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    normalizedQuantity,
                    normalizedChargeBasis,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate,
                    detail.BillToClient
                ),
                cancellationToken
            );

            if (!resolution.IsSuccess)
            {
                return Result.Failure(resolution.Error!);
            }

            resolvedDetails.Add(resolution.Detail!);
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

        var currentServiceIds = rate.RateServices.Select(x => x.ServiceId).OrderBy(x => x).ToArray();
        var requestedServiceIds = (command.Services ?? Array.Empty<RateServiceSelection>())
            .Select(x => x.Id).OrderBy(x => x).ToArray();
        var servicesChanged = !currentServiceIds.SequenceEqual(requestedServiceIds);
        var operationTypeChanged = rate.OperationType != command.OperationType;
        var fixedDetailsTouched = resolvedDetails.Any(x =>
            x.CostId.HasValue && x.CostType == CostType.Fixed);

        var selectorsChanged =
            rate.AgentId != agentId
            || rate.CarrierId != carrierId
            || rate.PolId != polId
            || rate.PoeId != poeId
            || rate.PodId != podId
            || containersChanged
            || rate.IncotermId != incotermId
            || rate.CurrencyId != currencyId
            || rate.ShipmentMode != command.ShipmentMode
            || servicesChanged
            || operationTypeChanged;

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
                SanitizeSubjectToForExcludedDetails(command.SubjectTo, extraDetails),
                command.Excludes,
                transitTime,
                command.RateType,
                command.UpdatedBy
            );
            rate.ConfigureExecutive(command.ExecutiveName);
            rate.ConfigureCommercialPresentation(command.UseAllInPresentation, command.UpdatedBy);
            if (command.FinalBackupStorageIds is not null)
                rate.ConfigureFinalBackupStorageIds(command.FinalBackupStorageIds);
            rate.SetOperationType(command.OperationType, command.UpdatedBy);
            rate.ConfigureServices(command.Services, command.UpdatedBy);
            var pickupAddress = ResolvePickupAddress(
                command.ShipmentMode,
                command.IncotermName,
                command.IncotermCode,
                command.PickupAddress,
                command.CargoLines,
                rate.PickupAddress,
                rate.CargoLinesJson
            );
            var preserveExistingPickupLocation =
                command.ShipmentMode == ShipmentMode.Lcl
                && !string.IsNullOrWhiteSpace(pickupAddress)
                && string.Equals(pickupAddress, rate.PickupAddress, StringComparison.OrdinalIgnoreCase);

            rate.ConfigurePickupLocation(
                command.WarehouseId
                    ?? (preserveExistingPickupLocation && IsIncoterm(command.IncotermName, command.IncotermCode, "FCA")
                        ? rate.WarehouseId
                        : null),
                pickupAddress,
                command.PickupLatitude
                    ?? (preserveExistingPickupLocation ? rate.PickupLatitude : null),
                command.PickupLongitude
                    ?? (preserveExistingPickupLocation ? rate.PickupLongitude : null)
            );

            if (command.ExchangeRateApplied is > 0m || command.ExchangeRateSale is > 0m)
            {
                var appliedRate = command.ExchangeRateApplied is > 0m
                    ? command.ExchangeRateApplied.Value
                    : command.ExchangeRateSale!.Value;
                var purchaseRate = command.ExchangeRatePurchase is > 0m
                    ? command.ExchangeRatePurchase
                    : rate.ExchangeRatePurchase;
                var saleRate = command.ExchangeRateSale is > 0m
                    ? command.ExchangeRateSale
                    : rate.ExchangeRateSale;
                var exchangeChanged =
                    purchaseRate != rate.ExchangeRatePurchase
                    || saleRate != rate.ExchangeRateSale
                    || appliedRate != rate.ExchangeRateApplied;

                rate.ConfigureExchangeRateSnapshot(
                    purchaseRate,
                    saleRate,
                    appliedRate,
                    exchangeChanged ? DateTime.UtcNow.Date : rate.ExchangeRateDate,
                    DateTime.UtcNow,
                    exchangeChanged ? "Wizard Pricing · ajuste manual" : rate.ExchangeRateSource ?? "Wizard Pricing",
                    exchangeChanged || rate.ExchangeRateManualOverride,
                    command.UpdatedBy
                );
            }

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

                    var modified = rate.RateDetails.First(x => x.Id == detail.Id.Value);
                    modified.ConfigureDestinationTax(
                        detail.ApplyDestinationTax,
                        detail.DestinationTaxRate
                    );
                    modified.ConfigureBillToClient(detail.BillToClient);
                    modifiedDetails.Add(modified);
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

                    added.ConfigureDestinationTax(
                        detail.ApplyDestinationTax,
                        detail.DestinationTaxRate
                    );
                    added.ConfigureBillToClient(detail.BillToClient);
                    addedDetails.Add(added);
                }
            }

            // Los costos fijos dependen de naviera, agente, ruta, incoterm y modo de embarque.
            // Primero aplicamos los cambios del payload sobre los detalles existentes y después
            // resincronizamos. De lo contrario SynchronizeAsync elimina los detalles fijos antiguos
            // y el bucle anterior intenta actualizar sus IDs ya eliminados, produciendo
            // "El detalle de la tarifa no existe" al cambiar, por ejemplo, la naviera.
            if (selectorsChanged || fixedDetailsTouched || command.ShipmentMode is ShipmentMode.Lcl or ShipmentMode.Ltl)
            {
                await fixedCostSynchronizer.SynchronizeAsync(
                    rate,
                    command.UpdatedBy,
                    cancellationToken,
                    preserveExplicitFixedDetails: command.SourceImportFclRateId.HasValue
                );

                // During an edit, the persisted detail snapshot is authoritative while the
                // route/provider selectors remain unchanged. Synchronization may refresh existing
                // automatic fixed rows, but it must not resurrect a catalog cost that the user
                // removed from Pantalla 7 (for example "Retiro Vacío (Merchant)").
                var explicitAutomaticFixedCostIds = resolvedDetails
                    .Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
                    .Select(x => x.CostId!.Value)
                    .ToHashSet();

                HashSet<Guid> disallowedAutomaticFixedCostIds;
                if (selectorsChanged)
                {
                    disallowedAutomaticFixedCostIds = explicitlyRemovedAutomaticFixedCostIds;
                }
                else
                {
                    var allowedAutomaticFixedCostIds = automaticFixedCostIdsBeforeUpdate
                        .Concat(explicitAutomaticFixedCostIds)
                        .ToHashSet();
                    allowedAutomaticFixedCostIds.ExceptWith(explicitlyRemovedAutomaticFixedCostIds);

                    disallowedAutomaticFixedCostIds = rate.RateDetails
                        .Where(IsAutomaticFixed)
                        .Select(x => x.CostId!.Value)
                        .Where(costId => !allowedAutomaticFixedCostIds.Contains(costId))
                        .ToHashSet();
                }

                if (disallowedAutomaticFixedCostIds.Count > 0)
                {
                    var resurrectedDetailIds = rate.RateDetails
                        .Where(detail =>
                            IsAutomaticFixed(detail)
                            && disallowedAutomaticFixedCostIds.Contains(detail.CostId!.Value))
                        .Select(detail => detail.Id)
                        .ToArray();

                    foreach (var detailId in resurrectedDetailIds)
                    {
                        rate.RemoveRateDetail(detailId, command.UpdatedBy);
                    }
                }

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
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("Config.", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Config devolvió", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("de Config", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Result.Failure(PricingErrors.ConfigServiceUnavailable);
        }
        catch (InvalidOperationException exception)
        {
            // Antes cualquier validación de datos terminaba reportándose como un error
            // de estado, ocultando la causa real al frontend.
            return Result.Failure(PricingErrors.RateUpdateValidationFailed(exception.Message));
        }

        if (acceptedRevision is not null)
        {
            await rateRevisions.AddAsync(
                RateRevision.Create(
                    rate.Id, acceptedRevisionNumber, acceptedRevision.Status, acceptedRevision.RateName,
                    acceptedRevision.IdtraNumber, acceptedRevision.QuoNumber, acceptedRevision.TotalSaleUsd,
                    acceptedRevision.TotalSaleCrc, acceptedRevision.MarginPercentage, acceptedRevision.Json, command.UpdatedBy
                ),
                cancellationToken
            );
            rate.BeginRevision(command.UpdatedBy);
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
                    UpdateReason = command.UpdateReason.Trim(),
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

    private static string? ResolvePickupAddress(
        ShipmentMode shipmentMode,
        string? incotermName,
        string? incotermCode,
        string? requestedPickupAddress,
        IReadOnlyCollection<RateCargoLineCommandItem> cargoLines,
        string? existingPickupAddress,
        string? existingCargoLinesJson
    )
    {
        if (!IsPickupIncoterm(incotermName, incotermCode))
            return null;

        if (!string.IsNullOrWhiteSpace(requestedPickupAddress))
            return requestedPickupAddress.Trim();

        if (shipmentMode != ShipmentMode.Lcl)
            return null;

        foreach (var line in cargoLines)
        {
            var extracted = ExtractPickupAddress(line.Description);
            if (!string.IsNullOrWhiteSpace(extracted))
                return extracted;
        }

        foreach (var line in RateCargoProfileFactory.Deserialize(existingCargoLinesJson))
        {
            var extracted = ExtractPickupAddress(line.Description);
            if (!string.IsNullOrWhiteSpace(extracted))
                return extracted;
        }

        // LCL edits must be lossless. If the current UI did not send the pickup
        // again, keep the dedicated value already persisted on the quotation.
        return string.IsNullOrWhiteSpace(existingPickupAddress)
            ? null
            : existingPickupAddress.Trim();
    }

    private static bool IsPickupIncoterm(string? incotermName, string? incotermCode) =>
        IsIncoterm(incotermName, incotermCode, "EXW")
        || IsIncoterm(incotermName, incotermCode, "FCA");

    private static bool IsIncoterm(string? incotermName, string? incotermCode, string expected)
    {
        var value = $"{incotermName} {incotermCode}";
        return value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPickupAddress(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var markers = new[] { "Recolecta:", "Recolección:", "Recoleccion:", "Pickup:" };
        foreach (var marker in markers)
        {
            var markerIndex = description.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            var value = description[(markerIndex + marker.Length)..].Trim();
            var stop = value.Length;
            foreach (var separator in new[] { "\r", "\n", " · ", " | " })
            {
                var separatorIndex = value.IndexOf(separator, StringComparison.Ordinal);
                if (separatorIndex >= 0 && separatorIndex < stop)
                    stop = separatorIndex;
            }

            value = value[..stop].Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }


    private static CostDetailType NormalizeLclDetailType(
        ShipmentMode shipmentMode,
        string? name,
        CostDetailType detailType
    )
    {
        if (shipmentMode != ShipmentMode.Lcl)
            return detailType;

        var normalizedName = NormalizeLclDetailName(name);
        return normalizedName is "PICK UP" or "PICKUP" or "RECOLECTA" or "RECOLECCION"
            ? CostDetailType.OriginCharge
            : detailType;
    }

    private static ChargeBasis? NormalizeLclChargeBasis(
        ShipmentMode shipmentMode,
        string? name,
        ChargeBasis? chargeBasis
    )
    {
        if (shipmentMode != ShipmentMode.Lcl)
            return chargeBasis;

        var normalizedName = NormalizeLclDetailName(name);
        if (normalizedName == "CFS")
            return ChargeBasis.PerCbm;

        if (normalizedName is "PICK UP" or "PICKUP" or "RECOLECTA" or "RECOLECCION")
            return ChargeBasis.PerShipment;

        return chargeBasis;
    }

    private static decimal? NormalizeLclDetailQuantity(
        ShipmentMode shipmentMode,
        string? name,
        decimal? quantity,
        decimal totalVolumeCbm
    )
    {
        if (shipmentMode != ShipmentMode.Lcl)
            return quantity;

        var normalizedName = NormalizeLclDetailName(name);
        if (normalizedName == "CFS")
            return totalVolumeCbm > 0m ? totalVolumeCbm : quantity;

        if (normalizedName is "PICK UP" or "PICKUP" or "RECOLECTA" or "RECOLECCION")
            return 1m;

        return quantity;
    }

    private static string NormalizeLclDetailName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return name.Trim()
            .ToUpperInvariant()
            .Replace("Ó", "O", StringComparison.Ordinal)
            .Replace("Í", "I", StringComparison.Ordinal)
            .Replace("Á", "A", StringComparison.Ordinal)
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("Ú", "U", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal);
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
            && freight[0].Quantity.GetValueOrDefault() == 0m
        )
            return true;
        if (freight.Any(x => !x.Quantity.HasValue || x.Quantity.Value <= 0)) return false;

        return freight.Sum(x => x.Quantity!.Value) == containers.Sum(x => x.Quantity);
    }

    private static string? SanitizeSubjectToForExcludedDetails(
        string? subjectTo,
        IReadOnlyCollection<UpsertRateExtraDetailCommandItem> details
    )
    {
        if (string.IsNullOrWhiteSpace(subjectTo)) return subjectTo;
        if (details.Any(detail => IsEmptyContainerReturn(detail.Name))) return subjectTo;

        var lines = subjectTo
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !IsEmptyContainerReturn(line))
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static bool IsEmptyContainerReturn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal)
            .Replace("ü", "u", StringComparison.Ordinal);

        return normalized.Contains("retiro vacio", StringComparison.Ordinal)
            || normalized.Contains("retiro de vacio", StringComparison.Ordinal);
    }

    private static bool IsAutomaticFixed(RateDetail detail)
    {
        return detail.CostId.HasValue && detail.CostType == CostType.Fixed;
    }
}
