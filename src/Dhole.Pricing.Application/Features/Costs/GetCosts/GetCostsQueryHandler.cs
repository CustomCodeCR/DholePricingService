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
            query.CostTypes,
            query.CostDetailTypes,
            query.CarrierIds,
            query.AgentIds,
            query.PortIds,
            query.PortRoles,
            query.CurrencyIds,
            query.ActiveStates,
            cancellationToken
        );

        return Result.Success(result);
    }
}
