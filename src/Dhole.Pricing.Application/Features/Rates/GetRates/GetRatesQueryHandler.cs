using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRates;

public sealed class GetRatesQueryHandler(IRateHeaderRepository rateHeaders)
    : IQueryHandler<GetRatesQuery, Result<PagedResult<RateDto>>>
{
    public async Task<Result<PagedResult<RateDto>>> HandleAsync(
        GetRatesQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await rateHeaders.GetPagedAsync(
            query.Page,
            query.Search,
            query.SourceImportFclRateId,
            query.AgentId,
            query.CarrierId,
            query.PolId,
            query.PoeId,
            query.PodId,
            query.ContainerTypeId,
            query.CurrencyId,
            query.Status,
            query.RequiredApproval,
            query.QuoteDate,
            query.ValidFrom,
            query.ValidTo,
            cancellationToken
        );

        return Result.Success(result);
    }
}
