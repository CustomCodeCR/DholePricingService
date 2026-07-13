using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRates;

public sealed class GetImportRatesQueryHandler(IImportFclRateRepository importRates)
    : IQueryHandler<GetImportRatesQuery, Result<PagedResult<ImportRateDto>>>
{
    public async Task<Result<PagedResult<ImportRateDto>>> HandleAsync(
        GetImportRatesQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await importRates.GetPagedAsync(
            query.Page,
            query.Search,
            query.ImportBatchId,
            query.SourceType,
            query.Status,
            query.Pol,
            query.Pod,
            query.Carrier,
            query.ContainerType,
            query.Currency,
            query.QuoteDate,
            query.ValidFrom,
            query.ValidTo,
            cancellationToken
        );

        return Result.Success(result);
    }
}
