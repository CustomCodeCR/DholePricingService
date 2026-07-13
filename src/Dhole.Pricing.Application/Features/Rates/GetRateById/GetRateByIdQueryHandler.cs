using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GetRateById;

public sealed class GetRateByIdQueryHandler(
    IRateHeaderRepository rateHeaders,
    IRateHeaderCacheService cache
) : IQueryHandler<GetRateByIdQuery, Result<RateDto>>
{
    public async Task<Result<RateDto>> HandleAsync(
        GetRateByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetRateHeaderByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var rate = await rateHeaders.GetByIdWithDetailsAsync(query.Id, cancellationToken);

        if (rate is null || rate.IsDeleted)
        {
            return Result.Failure<RateDto>(PricingErrors.RateHeaderNotFound);
        }

        var dto = rate.ToDto();

        await cache.SetRateHeaderByIdAsync(rate.Id, dto, cancellationToken: cancellationToken);

        return Result.Success(dto);
    }
}
