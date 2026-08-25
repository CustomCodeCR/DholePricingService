using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;

public sealed class GetImportRatesForSelectQueryHandler(IImportFclRateRepository importRates)
    : IQueryHandler<GetImportRatesForSelectQuery, Result<IReadOnlyCollection<ImportRateSelectDto>>>
{
    public async Task<Result<IReadOnlyCollection<ImportRateSelectDto>>> HandleAsync(
        GetImportRatesForSelectQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await importRates.GetForSelectAsync(
            query.Search,
            query.ImportBatchId,
            query.SourceType,
            ImportStatus.Approved,
            query.Agent,
            query.Carrier,
            query.Pol,
            query.Poe,
            query.Pod,
            query.ContainerType,
            query.Currency,
            query.QuoteDate,
            cancellationToken
        );

        return Result.Success<IReadOnlyCollection<ImportRateSelectDto>>(
            result.Where(x => string.Equals(x.Status, nameof(ImportStatus.Approved), StringComparison.OrdinalIgnoreCase)).ToArray()
        );
    }
}
