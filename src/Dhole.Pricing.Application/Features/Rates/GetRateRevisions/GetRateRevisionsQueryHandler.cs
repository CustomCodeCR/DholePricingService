using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GetRateRevisions;

public sealed class GetRateRevisionsQueryHandler(IRateHeaderRepository rates, IRateRevisionRepository revisions)
    : IQueryHandler<GetRateRevisionsQuery, Result<IReadOnlyCollection<RateRevisionDto>>>
{
    public async Task<Result<IReadOnlyCollection<RateRevisionDto>>> HandleAsync(GetRateRevisionsQuery query, CancellationToken cancellationToken = default)
    {
        var rate = await rates.GetByIdWithDetailsAsync(query.RateHeaderId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Result.Failure<IReadOnlyCollection<RateRevisionDto>>(PricingErrors.RateHeaderNotFound);

        var items = await revisions.GetByRateHeaderIdAsync(query.RateHeaderId, cancellationToken);
        return Result.Success<IReadOnlyCollection<RateRevisionDto>>(items.Select(x => new RateRevisionDto(
            x.Id, x.RateHeaderId, x.RevisionNumber, x.Status, x.RateName, x.IdtraNumber, x.QuoNumber,
            x.TotalSaleUsd, x.TotalSaleCrc, x.MarginPercentage, x.CreatedAtUtc, x.CreatedBy, x.SnapshotJson)).ToList());
    }
}
