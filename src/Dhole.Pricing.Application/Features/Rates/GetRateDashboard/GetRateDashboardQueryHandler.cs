using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRateDashboard;

public sealed class GetRateDashboardQueryHandler(IRateHeaderRepository rateHeaders)
    : IQueryHandler<GetRateDashboardQuery, Result<PricingRateDashboardDto>>
{
    public async Task<Result<PricingRateDashboardDto>> HandleAsync(
        GetRateDashboardQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var dashboard = await rateHeaders.GetDashboardAsync(
            query.CreatedFrom,
            query.CreatedTo,
            query.ModifiedFrom,
            query.ModifiedTo,
            query.ValidityFrom,
            query.ValidityTo,
            cancellationToken
        );

        return Result.Success(dashboard);
    }
}
