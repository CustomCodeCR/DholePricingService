using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Contracts.Costs.Response;

namespace Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;

public sealed class GetCostsForSelectQueryHandler(
    ICostRepository costs,
    ICostCacheService cache,
    ICostRoutePortSelectionStore routePorts,
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog
) : IQueryHandler<GetCostsForSelectQuery, Result<IReadOnlyCollection<CostSelectDto>>>
{
    public async Task<Result<IReadOnlyCollection<CostSelectDto>>> HandleAsync(
        GetCostsForSelectQuery query,
        CancellationToken cancellationToken = default
    )
    {
        // "Multimodal Via Panamá" is a synthetic POE used by the wizard. When the
        // selected imported tariff is supplied, its real POE (and parties when missing
        // from the request) is the authoritative context for cost/surcharge matching.
        if (query.ApplicableToContext && query.ImportRateId.HasValue)
        {
            var importRate = await importRates.GetByIdAsync(query.ImportRateId.Value, cancellationToken);

            if (importRate is not null && !importRate.IsDeleted)
            {
                query = query with
                {
                    PoeId = importRate.PoeId != Guid.Empty ? importRate.PoeId : query.PoeId,
                    CarrierId = query.CarrierId ?? importRate.CarrierId,
                    AgentId = query.AgentId ?? importRate.AgentId,
                };
            }
        }

        var canUseGeneralCache = CanUseGeneralCache(query);

        if (canUseGeneralCache)
        {
            var cached = await cache.GetCostsSelectAsync(cancellationToken);

            if (cached is not null)
            {
                var cachedSelections = await routePorts.GetManyAsync(
                    cached.Select(item => item.Id).ToArray(),
                    cancellationToken
                );
                var hydratedCached = await HydrateRelationListsAsync(
                    cached,
                    cachedSelections,
                    configCatalog,
                    cancellationToken
                );
                return Result.Success(hydratedCached);
            }
        }

        // The wizard sends a complete pricing context. In that mode we must not apply the
        // old exact carrier/agent/port filters at repository level because they would drop
        // generic Cost rows (null condition) before applicability can be evaluated.
        var items = await costs.GetForSelectAsync(
            query.Search,
            query.CostType,
            query.CostDetailType,
            query.ApplicableToContext ? null : query.CarrierId,
            query.ApplicableToContext ? null : query.AgentId,
            query.ApplicableToContext ? null : query.PortId,
            query.ApplicableToContext ? null : query.PortRole,
            query.CurrencyId,
            query.IsActive,
            cancellationToken
        );

        // A shipment-mode-only lookup is also a real filter. Historically ShipmentMode
        // was evaluated only when ApplicableToContext=true, so /costs/select?shipmentMode=AirConsol
        // returned every active cost and forced the browser to recover the intended set.
        if (!query.ApplicableToContext && query.ShipmentMode.HasValue)
        {
            items = items
                .Where(item => ShipmentModeMatches(item, query, requireConfiguredContext: false))
                .ToArray();
        }

        var selections = await routePorts.GetManyAsync(
            items.Select(item => item.Id).ToArray(),
            cancellationToken
        );

        if (query.ApplicableToContext)
        {
            items = items
                .Where(item =>
                {
                    selections.TryGetValue(item.Id, out var selection);
                    return IsApplicableToContext(item, query, selection);
                })
                .OrderByDescending(CostSpecificity)
                .ThenBy(item => item.CostType)
                .ThenBy(item => item.CostDetailType)
                .ThenBy(item => item.Name)
                .ToArray();
        }

        items = await HydrateRelationListsAsync(
            items,
            selections,
            configCatalog,
            cancellationToken
        );

        if (canUseGeneralCache)
        {
            await cache.SetCostsSelectAsync(items, cancellationToken: cancellationToken);
        }

        return Result.Success(items);
    }

    private static bool IsApplicableToContext(
        CostSelectDto cost,
        GetCostsForSelectQuery query,
        CostRoutePortSelectionSet? selection
    )
    {
        // Optional charges are auto-selected by the wizard. Because of that, a configured
        // restriction must have a concrete matching value in the current quote before the
        // optional charge is returned. Generic optionals (without that restriction) remain
        // valid wildcards. Fixed/variable costs keep their historical wildcard behavior.
        var requireConfiguredContext = string.Equals(
            cost.CostType,
            "Optional",
            StringComparison.OrdinalIgnoreCase
        );

        if (!PartyMatches(selection?.CarrierIds, cost.CarrierId, query.CarrierId, requireConfiguredContext))
            return false;

        if (!PartyMatches(selection?.AgentIds, cost.AgentId, query.AgentId, requireConfiguredContext))
            return false;

        if (!RouteRoleMatches(selection?.PolIds, cost.PolId, query.PolId))
            return false;

        if (!RouteRoleMatches(selection?.PoeIds, cost.PoeId, query.PoeId))
            return false;

        if (!RouteRoleMatches(selection?.PodIds, cost.PodId, query.PodId))
            return false;

        if (cost.Incoterms.Count > 0)
        {
            if (!query.IncotermId.HasValue)
            {
                if (requireConfiguredContext)
                    return false;
            }
            else if (!cost.Incoterms.Any(incoterm => incoterm.Id == query.IncotermId.Value))
            {
                return false;
            }
        }

        if (cost.Services?.Count > 0)
        {
            if (query.ServiceIds is null || query.ServiceIds.Count == 0)
                return false;
            if (!cost.Services.Any(service => query.ServiceIds.Contains(service.Id)))
                return false;
        }

        if (!ShipmentModeMatches(cost, query, requireConfiguredContext))
            return false;

        if (cost.PortId.HasValue && !LegacyPortMatches(cost, query, selection))
            return false;

        return true;
    }

    private static bool PartyMatches(
        IReadOnlyCollection<Guid>? selectedPartyIds,
        Guid? legacyPartyId,
        Guid? contextPartyId,
        bool requireConfiguredContext
    )
    {
        if (!contextPartyId.HasValue)
        {
            var hasRestriction = selectedPartyIds is { Count: > 0 } || legacyPartyId.HasValue;
            return !requireConfiguredContext || !hasRestriction;
        }

        if (selectedPartyIds is { Count: > 0 })
            return selectedPartyIds.Contains(contextPartyId.Value);

        return !legacyPartyId.HasValue || legacyPartyId.Value == contextPartyId.Value;
    }

    private static bool RouteRoleMatches(
        IReadOnlyCollection<Guid>? selectedPortIds,
        Guid? legacyPortId,
        Guid? contextPortId
    )
    {
        if (!contextPortId.HasValue)
        {
            // Route restrictions are hard constraints for every cost type. A POD-specific
            // cost (for example Honduras) must not apply while the quote has no POD.
            var hasRestriction = selectedPortIds is { Count: > 0 } || legacyPortId.HasValue;
            return !hasRestriction;
        }

        if (selectedPortIds is { Count: > 0 })
            return selectedPortIds.Contains(contextPortId.Value);

        return !legacyPortId.HasValue || legacyPortId.Value == contextPortId.Value;
    }

    private static bool LegacyPortMatches(
        CostSelectDto cost,
        GetCostsForSelectQuery query,
        CostRoutePortSelectionSet? selection
    )
    {
        if (!cost.PortId.HasValue)
            return true;

        // Multi-port selections are authoritative. PortId is only a legacy
        // snapshot of the first configured port and must not reject another
        // POE/POL/POD explicitly selected in CostRoutePortSelections.
        var selectedPortsForRole = cost.PortRole?.ToLowerInvariant() switch
        {
            "pol" => selection?.PolIds,
            "poe" => selection?.PoeIds,
            "pod" => selection?.PodIds,
            _ => null,
        };
        if (selectedPortsForRole is { Count: > 0 })
            return true;

        return cost.PortRole?.ToLowerInvariant() switch
        {
            "pol" => query.PolId.HasValue && cost.PortId == query.PolId,
            "poe" => query.PoeId.HasValue && cost.PortId == query.PoeId,
            "pod" => query.PodId.HasValue && cost.PortId == query.PodId,
            _ =>
                (query.PolId.HasValue || query.PoeId.HasValue || query.PodId.HasValue)
                && (
                    cost.PortId == query.PolId
                    || cost.PortId == query.PoeId
                    || cost.PortId == query.PodId
                ),
        };
    }

    private static int CostSpecificity(CostSelectDto cost)
    {
        var score = 0;
        if (ConfiguredShipmentModes(cost).Count > 0) score += 2;
        if (cost.Incoterms.Count > 0) score += 2;
        if (cost.Services?.Count > 0) score += 2;
        if (cost.CarrierId.HasValue) score += 3;
        if (cost.AgentId.HasValue) score += 3;
        if (cost.PolId.HasValue) score += 4;
        if (cost.PoeId.HasValue) score += 4;
        if (cost.PodId.HasValue) score += 4;
        if (cost.PortId.HasValue) score += 4;
        if (!string.IsNullOrWhiteSpace(cost.PortRole) && !cost.PortRole.Equals("Any", StringComparison.OrdinalIgnoreCase))
            score += 1;
        return score;
    }

    private static async Task<IReadOnlyCollection<CostSelectDto>> HydrateRelationListsAsync(
        IReadOnlyCollection<CostSelectDto> items,
        IReadOnlyDictionary<Guid, CostRoutePortSelectionSet> selections,
        IPricingConfigCatalogClient configCatalog,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
            return items;

        var catalogTasks = new[]
        {
            configCatalog.GetActiveByGroupAsync("pol", cancellationToken),
            configCatalog.GetActiveByGroupAsync("poe", cancellationToken),
            configCatalog.GetActiveByGroupAsync("pod", cancellationToken),
            configCatalog.GetActiveByGroupAsync("carriers", cancellationToken),
            configCatalog.GetActiveByGroupAsync("agents", cancellationToken),
        };
        var catalogs = await Task.WhenAll(catalogTasks);

        var pol = catalogs[0].ToDictionary(x => x.Id);
        var poe = catalogs[1].ToDictionary(x => x.Id);
        var pod = catalogs[2].ToDictionary(x => x.Id);
        var carriers = catalogs[3].ToDictionary(x => x.Id);
        var agents = catalogs[4].ToDictionary(x => x.Id);

        return items.Select(item =>
        {
            selections.TryGetValue(item.Id, out var selection);

            return item with
            {
                Pols = BuildRelations(selection?.PolIds, item.PolId, item.PolName, item.PolCode, pol),
                Poes = BuildRelations(selection?.PoeIds, item.PoeId, item.PoeName, item.PoeCode, poe),
                Pods = BuildRelations(selection?.PodIds, item.PodId, item.PodName, item.PodCode, pod),
                Carriers = BuildRelations(
                    selection?.CarrierIds,
                    item.CarrierId,
                    item.CarrierName,
                    item.CarrierCode,
                    carriers
                ),
                Agents = BuildRelations(
                    selection?.AgentIds,
                    item.AgentId,
                    item.AgentName,
                    item.AgentCode,
                    agents
                ),
            };
        }).ToArray();
    }

    private static IReadOnlyCollection<CostRelationDto> BuildRelations(
        IReadOnlyCollection<Guid>? selectedIds,
        Guid? legacyId,
        string? legacyName,
        string? legacyCode,
        IReadOnlyDictionary<Guid, PricingConfigCatalogItem> catalog
    )
    {
        var ids = selectedIds is { Count: > 0 }
            ? selectedIds.Where(id => id != Guid.Empty).Distinct().ToArray()
            : legacyId.HasValue && legacyId.Value != Guid.Empty
                ? [legacyId.Value]
                : [];

        return ids
            .Select(id =>
            {
                if (catalog.TryGetValue(id, out var item))
                {
                    var name = string.IsNullOrWhiteSpace(item.Value)
                        ? item.Name
                        : item.Value.Trim();
                    return new CostRelationDto(id, name, item.Code);
                }

                if (legacyId == id)
                {
                    return new CostRelationDto(
                        id,
                        string.IsNullOrWhiteSpace(legacyName) ? id.ToString() : legacyName.Trim(),
                        legacyCode?.Trim() ?? string.Empty
                    );
                }

                return new CostRelationDto(id, id.ToString(), string.Empty);
            })
            .OrderBy(item => item.Name)
            .ToArray();
    }

    private static bool ShipmentModeMatches(
        CostSelectDto cost,
        GetCostsForSelectQuery query,
        bool requireConfiguredContext
    )
    {
        var configuredShipmentModes = ConfiguredShipmentModes(cost);

        // No configured mode means wildcard: the cost is intentionally reusable.
        if (configuredShipmentModes.Count == 0)
            return true;

        if (!query.ShipmentMode.HasValue)
            return !requireConfiguredContext;

        return configuredShipmentModes.Contains(
            query.ShipmentMode.Value.ToString(),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static IReadOnlyCollection<string> ConfiguredShipmentModes(CostSelectDto cost)
    {
        if (cost.ShipmentModes is { Count: > 0 })
            return cost.ShipmentModes;

        return string.IsNullOrWhiteSpace(cost.ShipmentMode)
            ? Array.Empty<string>()
            : new[] { cost.ShipmentMode! };
    }

    private static bool CanUseGeneralCache(GetCostsForSelectQuery query)
    {
        return !query.ApplicableToContext
            && string.IsNullOrWhiteSpace(query.Search)
            && !query.CostType.HasValue
            && !query.CostDetailType.HasValue
            && !query.CarrierId.HasValue
            && !query.AgentId.HasValue
            && !query.PortId.HasValue
            && !query.PortRole.HasValue
            && !query.CurrencyId.HasValue
            && !query.PolId.HasValue
            && !query.PoeId.HasValue
            && !query.PodId.HasValue
            && !query.IncotermId.HasValue
            && !query.ShipmentMode.HasValue
            && !query.ImportRateId.HasValue
            && (query.ServiceIds is null || query.ServiceIds.Count == 0)
            && query.IsActive == true;
    }
}
