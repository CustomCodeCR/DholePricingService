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

        var items = await costs.GetForSelectAsync(
            query.Search,
            query.CostType,
            query.CostDetailType,
            query.CarrierId,
            query.AgentId,
            query.PortId,
            query.PortRole,
            query.CurrencyId,
            query.IsActive,
            cancellationToken
        );

        if (canUseGeneralCache)
        {
            await cache.SetCostsSelectAsync(items, cancellationToken: cancellationToken);
        }

        return Result.Success(items);
    }

    private static bool CanUseGeneralCache(GetCostsForSelectQuery query)
    {
        return string.IsNullOrWhiteSpace(query.Search)
            && !query.CostType.HasValue
            && !query.CostDetailType.HasValue
            && !query.CarrierId.HasValue
            && !query.AgentId.HasValue
            && !query.PortId.HasValue
            && !query.PortRole.HasValue
            && !query.CurrencyId.HasValue
            && query.IsActive == true;
    }
}
