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
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

public sealed class DuplicateRateCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateCodeGenerator rateCodeGenerator,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IPricingConfigCatalogClient configCatalog,
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
            pod = await configCatalog.GetActiveInGroupAsync(
                source.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken);
            containerType = await configCatalog.GetActiveInGroupAsync(
                source.ContainerTypeId, PricingConstants.CatalogSlugs.ContainerTypes, cancellationToken);
            currency = await configCatalog.GetActiveInGroupAsync(
                source.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);

            if (pol is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El POL", PricingConstants.CatalogSlugs.Pol));
            if (poe is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El POE", PricingConstants.CatalogSlugs.Poe));
            if (pod is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
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

        RateHeader duplicate;

        try
        {
            duplicate = RateHeader.Create(
                rateCode,
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
                pod.Id,
                pod.SnapshotName(),
                pod.Code,
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
                command.ValidFrom ?? source.ValidFrom,
                command.ValidTo ?? source.ValidTo,
                source.ContainerQuantity > 0 ? source.ContainerQuantity : 1,
                source.ClientName,
                null,
                null,
                source.Includes,
                source.SubjectTo,
                source.Excludes,
                source.TransitTime,
                source.RateType,
                command.CreatedBy
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

            var copiedDetails = source.RateDetails.Where(x =>
                !x.CostId.HasValue || x.CostType != CostType.Fixed
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

                duplicate.AddRateDetail(
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
            }

            await fixedCostSynchronizer.SynchronizeAsync(
                duplicate,
                command.CreatedBy,
                cancellationToken
            );

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
