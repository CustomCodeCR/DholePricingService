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

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

public sealed class DuplicateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateCodeGenerator rateCodeGenerator,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IPricingConfigCatalogClient configCatalog,
    IPricingExchangeRateProvider exchangeRateProvider,
    IPricingAuditService audit,
    IRateHeaderCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DuplicateRateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        DuplicateRateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var source = await rateHeaders.GetByIdWithDetailsAsync(command.Id, cancellationToken);

        if (source is null || source.IsDeleted)
        {
            return Result.Failure<Guid>(PricingErrors.RateHeaderNotFound);
        }

        if (command.ApplyTariff)
        {
            var tariffApplicationDate = DateTime.UtcNow.Date;
            if (
                source.RateType != RateType.Tariff
                || source.SourceTariffRateId.HasValue
                || !string.IsNullOrWhiteSpace(source.ClientName)
                || source.Status is not (
                    RateStatus.ApprovedByManagement
                    or RateStatus.Open
                    or RateStatus.Sent
                    or RateStatus.RequestedByClient
                    or RateStatus.AcceptedByClient
                )
                || source.ValidFrom.Date > tariffApplicationDate
                || source.ValidTo.Date < tariffApplicationDate
            )
            {
                return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
            }

            var appliedClientName = string.IsNullOrWhiteSpace(command.ClientName)
                ? source.ClientName
                : command.ClientName.Trim();

            if (string.IsNullOrWhiteSpace(appliedClientName))
            {
                return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
            }

            var appliedRateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);
            RateHeader appliedRate;

            try
            {
                appliedRate = RateHeader.Create(
                    appliedRateCode,
                    source.SourceImportFclRateId,
                    source.AgentId,
                    source.AgentName,
                    source.AgentCode,
                    source.CarrierId,
                    source.CarrierName,
                    source.CarrierCode,
                    source.PolId,
                    source.PolName,
                    source.PolCode,
                    source.PoeId,
                    source.PoeName,
                    source.PoeCode,
                    source.PodId,
                    source.PodName,
                    source.PodCode,
                    source.ContainerTypeId,
                    source.ContainerTypeName,
                    source.ContainerTypeCode,
                    source.IncotermId,
                    source.IncotermName,
                    source.IncotermCode,
                    source.CurrencyId,
                    source.CurrencyName,
                    source.CurrencyCode,
                    source.FreeDays,
                    source.ValidFrom,
                    source.ValidTo,
                    source.ContainerQuantity > 0 ? source.ContainerQuantity : 1,
                    appliedClientName,
                    null,
                    appliedRateCode,
                    source.Includes,
                    source.SubjectTo,
                    source.Excludes,
                    source.TransitTime,
                    RateType.Tariff,
                    command.CreatedBy
                );

                appliedRate.ConfigureTariffSource(source.Id, source.RevisionNumber);
                appliedRate.ConfigureExecutive(
                    string.IsNullOrWhiteSpace(command.ExecutiveName)
                        ? source.ExecutiveName
                        : command.ExecutiveName.Trim()
                );
                appliedRate.ConfigurePickupLocation(
                    source.WarehouseId,
                    source.PickupAddress,
                    source.PickupLatitude,
                    source.PickupLongitude
                );

                var sourceAppliedExchangeRate = source.ExchangeRateApplied ?? source.ExchangeRateSale;
                if (sourceAppliedExchangeRate is > 0m)
                {
                    appliedRate.ConfigureExchangeRateSnapshot(
                        source.ExchangeRatePurchase,
                        source.ExchangeRateSale,
                        sourceAppliedExchangeRate.Value,
                        source.ExchangeRateDate,
                        source.ExchangeRateCapturedAtUtc ?? DateTime.UtcNow,
                        source.ExchangeRateSource ?? "Tarifario origen",
                        source.ExchangeRateManualOverride,
                        command.CreatedBy
                    );
                }

                var sourceContainers = source.RateContainers.Count > 0
                    ? source.RateContainers
                        .Select(x => new RateContainerAllocationSpec(
                            x.ContainerTypeId,
                            x.ContainerTypeName,
                            x.ContainerTypeCode,
                            x.Quantity
                        ))
                        .ToArray()
                    : new[]
                    {
                        new RateContainerAllocationSpec(
                            source.ContainerTypeId,
                            source.ContainerTypeName,
                            source.ContainerTypeCode,
                            source.ContainerQuantity > 0 ? source.ContainerQuantity : 1
                        )
                    };

                appliedRate.ReplaceContainerAllocations(sourceContainers, command.CreatedBy);
                appliedRate.ConfigureShipment(
                    source.ShipmentMode,
                    source.TotalPackages,
                    source.TotalPallets,
                    source.TotalWeightKg,
                    source.TotalVolumeCbm,
                    source.KgPerCbm,
                    source.CargoLinesJson,
                    command.CreatedBy
                );
                appliedRate.SetOperationType(source.OperationType, command.CreatedBy);
                appliedRate.ConfigureServices(
                    source.RateServices
                        .Select(x => new RateServiceSelection(
                            x.ServiceId,
                            x.ServiceName,
                            x.ServiceCode
                        ))
                        .ToArray(),
                    command.CreatedBy
                );
                appliedRate.ConfigureCommercialPresentation(
                    source.UseAllInPresentation,
                    command.CreatedBy
                );

                foreach (var detail in source.RateDetails)
                {
                    var copiedDetail = appliedRate.AddRateDetail(
                        appliedRate.Id,
                        detail.CostId,
                        detail.Name,
                        detail.CostDetailType,
                        detail.CostType,
                        detail.ChargeBasis,
                        detail.CurrencyId,
                        detail.CurrencyName,
                        detail.CurrencyCode,
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.Notes,
                        detail.Quantity > 0m ? detail.Quantity : 1m,
                        command.CreatedBy
                    );
                    copiedDetail.ConfigureDestinationTax(
                        detail.ApplyDestinationTax,
                        detail.DestinationTaxRate
                    );
                    copiedDetail.ConfigureBillToClient(detail.BillToClient);
                }

                // Un tarifario aplicado es un snapshot: no consulta fletes, Costs,
                // Config ni tipo de cambio nuevos. Conserva exactamente esta revisión,
                // pero nace como una QUO abierta para el cliente. La aceptación ocurre
                // después sobre la QUO hija y nunca sobre el tarifario maestro.
                appliedRate.SetAmounts(command.CreatedBy);
                appliedRate.PrepareTariffApplication(command.CreatedBy);
            }
            catch (InvalidOperationException)
            {
                return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
            }

            await rateHeaders.AddAsync(appliedRate, cancellationToken);

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderCreated,
                    Action: PricingAuditActions.Created,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: appliedRate.Id,
                    ActorUserId: command.CreatedBy,
                    After: PricingAuditSnapshots.From(appliedRate),
                    Payload: new
                    {
                        AppliedFromTariffRateHeaderId = source.Id,
                        SourceTariffRevisionNumber = source.RevisionNumber,
                        NewRateHeaderId = appliedRate.Id,
                        SourceQuoNumber = source.QuoNumber ?? source.RateCode,
                        NewQuoNumber = appliedRate.QuoNumber ?? appliedRate.RateCode,
                        SnapshotCopiedExactly = true,
                        appliedRate.ClientName,
                        Status = appliedRate.Status.ToString(),
                    }
                ),
                cancellationToken
            );

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveRateHeaderCacheAsync(appliedRate.Id, cancellationToken);

            return Result.Success(appliedRate.Id);
        }

        PricingConfigCatalogItem? agent;
        PricingConfigCatalogItem? carrier;
        PricingConfigCatalogItem? pol;
        PricingConfigCatalogItem? poe;
        PricingConfigCatalogItem? pod;
        PricingConfigCatalogItem? containerType;
        PricingConfigCatalogItem? incoterm = null;
        PricingConfigCatalogItem? currency;

        try
        {
            agent = await configCatalog.GetActiveInGroupAsync(
                source.AgentId, PricingConstants.CatalogSlugs.Agents, cancellationToken);
            if (source.AgentId.HasValue && agent is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El agente", PricingConstants.CatalogSlugs.Agents));

            carrier = await configCatalog.GetActiveInGroupAsync(
                source.CarrierId, PricingConstants.CatalogSlugs.Carriers, cancellationToken);
            if (source.CarrierId.HasValue && carrier is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "La naviera", PricingConstants.CatalogSlugs.Carriers));

            pol = await configCatalog.GetActiveInGroupAsync(
                source.PolId, PricingConstants.CatalogSlugs.Pol, cancellationToken);
            poe = await configCatalog.GetActiveInGroupAsync(
                source.PoeId, PricingConstants.CatalogSlugs.Poe, cancellationToken);
            pod = source.PodId.HasValue
                ? await configCatalog.GetActiveInGroupAsync(
                    source.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken)
                : null;
            containerType = await configCatalog.GetActiveInGroupAsync(
                source.ContainerTypeId, PricingConstants.CatalogSlugs.ContainerTypes, cancellationToken);
            currency = await configCatalog.GetActiveInGroupAsync(
                source.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);

            if (pol is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El POL", PricingConstants.CatalogSlugs.Pol));
            if (poe is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El POE", PricingConstants.CatalogSlugs.Poe));
            if (source.PodId.HasValue && pod is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POD", PricingConstants.CatalogSlugs.Pod));
            if (containerType is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El tipo de contenedor", PricingConstants.CatalogSlugs.ContainerTypes));
            if (currency is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "La moneda", PricingConstants.CatalogSlugs.Currencies));

            if (source.IncotermId.HasValue)
            {
                incoterm = await configCatalog.GetActiveInGroupAsync(
                    source.IncotermId, PricingConstants.CatalogSlugs.Incoterms, cancellationToken);
                if (incoterm is null)
                    return Result.Failure<Guid>(PricingErrors.RateInvalidIncoterm);
            }
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.ConfigServiceUnavailable);
        }

        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);
        var currentExchangeRate = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);
        var today = DateTime.UtcNow.Date;
        var duplicateValidFrom = command.ValidFrom
            ?? (source.ShipmentMode == ShipmentMode.Fcl ? today : source.ValidFrom);
        var duplicateValidTo = command.ValidTo
            ?? (source.ShipmentMode == ShipmentMode.Fcl ? duplicateValidFrom : source.ValidTo);

        RateHeader duplicate;

        try
        {
            duplicate = RateHeader.Create(
                rateCode,
                // Una copia FCL nunca debe conservar la tarifa importada anterior: el flete
                // debe seleccionarse otra vez para validar vigencia/disponibilidad actual.
                sourceImportFclRateId: null,
                agent?.Id,
                agent?.SnapshotName(),
                agent?.Code,
                carrier?.Id,
                carrier?.SnapshotName(),
                carrier?.Code,
                pol.Id,
                pol.SnapshotName(),
                pol.Code,
                poe.Id,
                poe.SnapshotName(),
                poe.Code,
                pod?.Id,
                pod?.SnapshotName(),
                pod?.Code,
                containerType.Id,
                containerType.SnapshotName(),
                containerType.Code,
                incoterm?.Id,
                incoterm?.SnapshotName(preferValue: true),
                incoterm?.Code,
                currency.Id,
                currency.SnapshotName(),
                currency.Code,
                source.FreeDays,
                duplicateValidFrom,
                duplicateValidTo,
                source.ContainerQuantity > 0 ? source.ContainerQuantity : 1,
                source.ClientName,
                null,
                rateCode,
                source.Includes,
                source.SubjectTo,
                source.Excludes,
                source.TransitTime,
                source.RateType,
                command.CreatedBy
            );
            duplicate.ConfigureExecutive(source.ExecutiveName);
            var duplicateAppliedExchangeRate = currentExchangeRate?.Sale ?? source.ExchangeRateApplied;
            if (duplicateAppliedExchangeRate is > 0m)
            {
                duplicate.ConfigureExchangeRateSnapshot(
                    currentExchangeRate?.Purchase,
                    currentExchangeRate?.Sale,
                    duplicateAppliedExchangeRate.Value,
                    currentExchangeRate?.RateDate ?? source.ExchangeRateDate,
                    currentExchangeRate?.CapturedAtUtc ?? DateTime.UtcNow,
                    currentExchangeRate?.Source ?? source.ExchangeRateSource ?? "Manual",
                    currentExchangeRate is null ? source.ExchangeRateManualOverride : false,
                    command.CreatedBy
                );
            }
            duplicate.ConfigurePickupLocation(
                source.WarehouseId,
                source.PickupAddress,
                source.PickupLatitude,
                source.PickupLongitude
            );

            IReadOnlyCollection<(Guid ContainerTypeId, int Quantity)> requestedContainers =
                source.RateContainers.Count > 0
                    ? source.RateContainers
                        .Select(x => (x.ContainerTypeId, x.Quantity))
                        .ToArray()
                    : new[]
                    {
                        (source.ContainerTypeId, source.ContainerQuantity > 0 ? source.ContainerQuantity : 1)
                    };

            var sourceContainers = new List<RateContainerAllocationSpec>();
            foreach (var requested in requestedContainers)
            {
                var resolvedContainer = await configCatalog.GetActiveInGroupAsync(
                    requested.ContainerTypeId, PricingConstants.CatalogSlugs.ContainerTypes, cancellationToken);
                if (resolvedContainer is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        "El tipo de contenedor", PricingConstants.CatalogSlugs.ContainerTypes));

                sourceContainers.Add(new RateContainerAllocationSpec(
                    resolvedContainer.Id, resolvedContainer.SnapshotName(), resolvedContainer.Code, requested.Quantity));
            }
            duplicate.ReplaceContainerAllocations(sourceContainers, command.CreatedBy);
            duplicate.ConfigureShipment(
                source.ShipmentMode,
                source.TotalPackages,
                source.TotalPallets,
                source.TotalWeightKg,
                source.TotalVolumeCbm,
                source.KgPerCbm,
                source.CargoLinesJson,
                command.CreatedBy
            );

            // Para FCL se conservan únicamente rubros verdaderamente manuales que no sean
            // flete. Todo rubro ligado al catálogo (fijo, variable u opcional) se vuelve a
            // resolver en el wizard con la naviera/flete escogidos y los costos vigentes.
            // Otros modos mantienen el comportamiento histórico de duplicación.
            var copiedDetails = source.RateDetails.Where(x =>
                source.ShipmentMode == ShipmentMode.Fcl
                    ? !x.CostId.HasValue && x.CostDetailType != CostDetailType.Freight
                    : !x.CostId.HasValue || x.CostType != CostType.Fixed
            );

            var detailCurrencies = new Dictionary<Guid, PricingConfigCatalogItem>();
            foreach (var detail in copiedDetails)
            {
                if (!detailCurrencies.TryGetValue(detail.CurrencyId, out var detailCurrency))
                {
                    detailCurrency = await configCatalog.GetActiveInGroupAsync(
                        detail.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);
                    if (detailCurrency is null)
                        return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                            "La moneda del detalle", PricingConstants.CatalogSlugs.Currencies));
                    detailCurrencies[detail.CurrencyId] = detailCurrency;
                }

                var copiedDetail = duplicate.AddRateDetail(
                    duplicate.Id,
                    detail.CostId,
                    detail.Name,
                    detail.CostDetailType,
                    detail.CostType,
                    detail.ChargeBasis,
                    detailCurrency.Id,
                    detailCurrency.SnapshotName(),
                    detailCurrency.Code,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    detail.Quantity > 0 ? detail.Quantity : 1,
                    command.CreatedBy
                );
                copiedDetail.ConfigureBillToClient(detail.BillToClient);
            }

            // En FCL no cargamos costos automáticos antes de escoger el nuevo flete.
            // Esos cargos/recargos pueden depender de la naviera, POE, POD, contenedor u
            // otras variables de la nueva opción y deben reconstruirse después de la selección.
            // Para los demás modos sí sincronizamos inmediatamente contra Costs vigente.
            if (source.ShipmentMode != ShipmentMode.Fcl)
            {
                await fixedCostSynchronizer.SynchronizeAsync(
                    duplicate,
                    command.CreatedBy,
                    cancellationToken
                );
            }

            duplicate.SetAmounts(command.CreatedBy);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("Config.", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("Config devolvió", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("catálogo 'currencies' de Config", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Result.Failure<Guid>(PricingErrors.ConfigServiceUnavailable);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.RateInvalidStatus);
        }

        await rateHeaders.AddAsync(duplicate, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: duplicate.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(duplicate),
                Payload: new
                {
                    SourceRateHeaderId = source.Id,
                    NewRateHeaderId = duplicate.Id,
                    RequiresFreightReselection = source.ShipmentMode == ShipmentMode.Fcl,
                    CostsRefreshDeferred = source.ShipmentMode == ShipmentMode.Fcl,
                    duplicate.TotalCostAmount,
                    duplicate.TotalSaleAmount,
                    duplicate.TotalUtilityAmount,
                    duplicate.MarginPercentage,
                    duplicate.RequiredApproval,
                    Status = duplicate.Status.ToString(),
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveRateHeaderCacheAsync(duplicate.Id, cancellationToken);

        return Result.Success(duplicate.Id);
    }
}
