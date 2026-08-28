using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.Create;

public sealed class CreateCostCommandHandler(
    ICostRepository costs,
    IPricingConfigCatalogClient configCatalog,
    IPricingAuditService audit,
    ICostCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateCostCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateCostCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var carrier = await configCatalog.GetActiveInGroupAsync(
                command.CarrierId, PricingConstants.CatalogSlugs.Carriers, cancellationToken);
            if (command.CarrierId.HasValue && command.CarrierId.Value != Guid.Empty && carrier is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "La naviera", PricingConstants.CatalogSlugs.Carriers));

            var agent = await configCatalog.GetActiveInGroupAsync(
                command.AgentId, PricingConstants.CatalogSlugs.Agents, cancellationToken);
            if (command.AgentId.HasValue && command.AgentId.Value != Guid.Empty && agent is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El agente", PricingConstants.CatalogSlugs.Agents));

            var pol = await configCatalog.GetActiveInGroupAsync(
                command.PolId, PricingConstants.CatalogSlugs.Pol, cancellationToken);
            if (command.PolId.HasValue && command.PolId.Value != Guid.Empty && pol is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POL", PricingConstants.CatalogSlugs.Pol));

            var poe = await configCatalog.GetActiveInGroupAsync(
                command.PoeId, PricingConstants.CatalogSlugs.Poe, cancellationToken);
            if (command.PoeId.HasValue && command.PoeId.Value != Guid.Empty && poe is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POE", PricingConstants.CatalogSlugs.Poe));

            var pod = await configCatalog.GetActiveInGroupAsync(
                command.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken);
            if (command.PodId.HasValue && command.PodId.Value != Guid.Empty && pod is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POD", PricingConstants.CatalogSlugs.Pod));

            PricingConfigCatalogItem? port = null;
            var hasStructuredRoute = pol is not null || poe is not null || pod is not null;
            if (!hasStructuredRoute && command.PortId.HasValue && command.PortId.Value != Guid.Empty)
            {
                IReadOnlyCollection<string> acceptedPortGroups = command.PortRole switch
                {
                    Dhole.Pricing.Domain.Costs.Enums.CostPortRole.Pol => [PricingConstants.CatalogSlugs.Pol],
                    Dhole.Pricing.Domain.Costs.Enums.CostPortRole.Poe => [PricingConstants.CatalogSlugs.Poe],
                    Dhole.Pricing.Domain.Costs.Enums.CostPortRole.Pod => [PricingConstants.CatalogSlugs.Pod],
                    _ => [PricingConstants.CatalogSlugs.Pol, PricingConstants.CatalogSlugs.Poe, PricingConstants.CatalogSlugs.Pod],
                };
                port = await configCatalog.GetActiveInAnyGroupAsync(
                    command.PortId, acceptedPortGroups, cancellationToken);
                if (port is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        "El puerto", string.Join("/", acceptedPortGroups)));
            }

            var currency = await configCatalog.GetActiveInGroupAsync(
                command.CurrencyId, PricingConstants.CatalogSlugs.Currencies, cancellationToken);
            if (currency is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "La moneda", PricingConstants.CatalogSlugs.Currencies));

            var normalizedIncoterms = new List<Dhole.Pricing.Domain.Costs.Entities.CostIncotermSelection>();
            foreach (var selected in command.Incoterms ?? Array.Empty<Dhole.Pricing.Domain.Costs.Entities.CostIncotermSelection>())
            {
                var incoterm = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.Incoterms, cancellationToken);
                if (incoterm is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        "El Incoterm", PricingConstants.CatalogSlugs.Incoterms));
                normalizedIncoterms.Add(new Dhole.Pricing.Domain.Costs.Entities.CostIncotermSelection(
                    incoterm.Id, incoterm.SnapshotName(preferValue: true), incoterm.Code));
            }

            var normalizedServices = new List<CostServiceSelection>();
            foreach (var selected in command.Services ?? Array.Empty<CostServiceSelection>())
            {
                var service = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.PricingServices, cancellationToken);
                if (service is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        "El servicio de Pricing", PricingConstants.CatalogSlugs.PricingServices));
                normalizedServices.Add(new CostServiceSelection(
                    service.Id, service.SnapshotName(preferValue: true), service.Code));
            }

            command = command with
            {
                CarrierId = carrier?.Id,
                CarrierName = carrier?.SnapshotName(),
                CarrierCode = carrier?.Code,
                AgentId = agent?.Id,
                AgentName = agent?.SnapshotName(),
                AgentCode = agent?.Code,
                PortId = hasStructuredRoute ? null : port?.Id,
                PortName = hasStructuredRoute ? null : port?.SnapshotName(),
                PortCode = hasStructuredRoute ? null : port?.Code,
                PolId = pol?.Id,
                PolName = pol?.SnapshotName(),
                PolCode = pol?.Code,
                PoeId = poe?.Id,
                PoeName = poe?.SnapshotName(),
                PoeCode = poe?.Code,
                PodId = pod?.Id,
                PodName = pod?.SnapshotName(),
                PodCode = pod?.Code,
                CurrencyId = currency.Id,
                CurrencyName = currency.SnapshotName(),
                CurrencyCode = currency.Code,
                Incoterms = normalizedIncoterms,
                Services = normalizedServices,
            };
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.ConfigServiceUnavailable);
        }

        if (
            await costs.ExistsByNameAsync(
                command.Name,
                command.CostType,
                command.CostDetailType,
                command.PortId,
                command.PortRole,
                command.PolId,
                command.PoeId,
                command.PodId,
                command.CarrierId,
                command.AgentId,
                command.ShipmentMode,
                command.ChargeBasis,
                null,
                cancellationToken
            )
        )
        {
            return Result.Failure<Guid>(PricingErrors.CostAlreadyExists);
        }

        Cost cost;

        try
        {
            cost = Cost.Create(
                command.Name,
                command.CostType,
                command.CostDetailType,
                command.CarrierId,
                command.CarrierName,
                command.CarrierCode,
                command.AgentId,
                command.AgentName,
                command.AgentCode,
                command.PortId,
                command.PortName,
                command.PortCode,
                command.PortRole,
                command.PolId,
                command.PolName,
                command.PolCode,
                command.PoeId,
                command.PoeName,
                command.PoeCode,
                command.PodId,
                command.PodName,
                command.PodCode,
                command.Incoterms,
                command.CurrencyId,
                command.CurrencyName,
                command.CurrencyCode,
                command.CostAmount,
                command.SaleAmount,
                command.Notes,
                command.IsAccountant,
                command.ShipmentMode,
                command.ChargeBasis,
                command.MinimumCostAmount,
                command.MinimumSaleAmount,
                command.KgPerCbm,
                command.CreatedBy
            );
            cost.ConfigureServices(command.Services);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(PricingErrors.InvalidCost);
        }

        await costs.AddAsync(cost, cancellationToken);

        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.CostCreated,
                Action: PricingAuditActions.Created,
                EntityType: PricingAuditEntityTypes.Cost,
                EntityId: cost.Id,
                ActorUserId: command.CreatedBy,
                After: PricingAuditSnapshots.From(cost),
                Payload: PricingAuditSnapshots.From(cost)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveCostCacheAsync(cost.Id, cancellationToken);

        return Result.Success(cost.Id);
    }
}
