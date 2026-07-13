using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRateById;

public sealed class GetImportRateByIdQueryHandler(
    IImportFclRateRepository importRates,
    IImportRateCacheService cache
) : IQueryHandler<GetImportRateByIdQuery, Result<ImportRateDto>>
{
    public async Task<Result<ImportRateDto>> HandleAsync(
        GetImportRateByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetImportRateByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var importRate = await importRates.GetByIdAsync(query.Id, cancellationToken);

        if (importRate is null || importRate.IsDeleted)
        {
            return Result.Failure<ImportRateDto>(PricingErrors.ImportFclRateNotFound);
        }

        var dto = importRate.ToDto();

        await cache.SetImportRateByIdAsync(
            importRate.Id,
            dto,
            cancellationToken: cancellationToken
        );

        return Result.Success(dto);
    }
}
