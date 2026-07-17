using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Costs.GetCostById;

public sealed class GetCostByIdQueryHandler(ICostRepository costs, ICostCacheService cache)
    : IQueryHandler<GetCostByIdQuery, Result<CostDto>>
{
    public async Task<Result<CostDto>> HandleAsync(
        GetCostByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetCostByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var cost = await costs.GetByIdAsync(query.Id, cancellationToken);

        if (cost is null || cost.IsDeleted)
        {
            return Result.Failure<CostDto>(PricingErrors.CostNotFound);
        }

        var dto = new CostDto(
            cost.Id,
            cost.Name,
            cost.CostType.ToString(),
            cost.CostDetailType.ToString(),
            cost.CarrierId,
            cost.CarrierName,
            cost.CarrierCode,
            cost.AgentId,
            cost.AgentName,
            cost.AgentCode,
            cost.PortId,
            cost.PortName,
            cost.PortCode,
            cost.PortRole.HasValue ? cost.PortRole.Value.ToString() : null,
            cost.CurrencyId,
            cost.CurrencyName,
            cost.CurrencyCode,
            cost.CostAmount,
            cost.SaleAmount,
            cost.UtilityAmount,
            cost.Notes!,
            cost.IsAccountant,
            cost.IsActive
        );

        await cache.SetCostByIdAsync(cost.Id, dto, cancellationToken: cancellationToken);

        return Result.Success(dto);
    }
}
