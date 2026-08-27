using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Costs.Response;

namespace Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;

public sealed class GetCostsForSelectQueryHandler(ICostRepository costs, ICostCacheService cache)
    : IQueryHandler<GetCostsForSelectQuery, Result<IReadOnlyCollection<CostSelectDto>>>
{
    public async Task<Result<IReadOnlyCollection<CostSelectDto>>> HandleAsync(
        GetCostsForSelectQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var canUseGeneralCache = CanUseGeneralCache(query);

        if (canUseGeneralCache)
        {
            var cached = await cache.GetCostsSelectAsync(cancellationToken);

            if (cached is not null)
            {
                return Result.Success(cached);
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

        if (query.ApplicableToContext)
        {
            items = items
                .Where(item => IsApplicableToContext(item, query))
                .OrderByDescending(CostSpecificity)
                .ThenBy(item => item.CostType)
                .ThenBy(item => item.CostDetailType)
                .ThenBy(item => item.Name)
                .ToArray();
        }

        if (canUseGeneralCache)
        {
            await cache.SetCostsSelectAsync(items, cancellationToken: cancellationToken);
        }

        return Result.Success(items);
    }

    private static bool IsApplicableToContext(CostSelectDto cost, GetCostsForSelectQuery query)
    {
        if (query.CarrierId.HasValue && cost.CarrierId.HasValue && cost.CarrierId != query.CarrierId)
            return false;

        if (query.AgentId.HasValue && cost.AgentId.HasValue && cost.AgentId != query.AgentId)
            return false;

        if (query.PolId.HasValue && cost.PolId.HasValue && cost.PolId != query.PolId)
            return false;

        if (query.PoeId.HasValue && cost.PoeId.HasValue && cost.PoeId != query.PoeId)
            return false;

        if (query.PodId.HasValue && cost.PodId.HasValue && cost.PodId != query.PodId)
            return false;

        if (
            query.IncotermId.HasValue
            && cost.Incoterms.Count > 0
            && !cost.Incoterms.Any(incoterm => incoterm.Id == query.IncotermId.Value)
        )
        {
            return false;
        }

        if (
            query.ShipmentMode.HasValue
            && !string.IsNullOrWhiteSpace(cost.ShipmentMode)
            && !string.Equals(
                cost.ShipmentMode,
                query.ShipmentMode.Value.ToString(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        if (cost.PortId.HasValue && !LegacyPortMatches(cost, query))
            return false;

        return true;
    }

    private static bool LegacyPortMatches(CostSelectDto cost, GetCostsForSelectQuery query)
    {
        if (!cost.PortId.HasValue)
            return true;

        return cost.PortRole?.ToLowerInvariant() switch
        {
            "pol" => !query.PolId.HasValue || cost.PortId == query.PolId,
            "poe" => !query.PoeId.HasValue || cost.PortId == query.PoeId,
            "pod" => !query.PodId.HasValue || cost.PortId == query.PodId,
            _ =>
                (!query.PolId.HasValue && !query.PoeId.HasValue && !query.PodId.HasValue)
                || cost.PortId == query.PolId
                || cost.PortId == query.PoeId
                || cost.PortId == query.PodId,
        };
    }

    private static int CostSpecificity(CostSelectDto cost)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(cost.ShipmentMode)) score += 2;
        if (cost.Incoterms.Count > 0) score += 2;
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
            && query.IsActive == true;
    }
}
