using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Costs.Response;

namespace Dhole.Pricing.Application.Features.Costs.GetCosts;

public sealed class GetCostsQueryHandler(ICostRepository costs)
    : IQueryHandler<GetCostsQuery, Result<PagedResult<CostDto>>>
{
    public async Task<Result<PagedResult<CostDto>>> HandleAsync(
        GetCostsQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await costs.GetPagedAsync(
            query.Page,
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

        return Result.Success(result);
    }
}
