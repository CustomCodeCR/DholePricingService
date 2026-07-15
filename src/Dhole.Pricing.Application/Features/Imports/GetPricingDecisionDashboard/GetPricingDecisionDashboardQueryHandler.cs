using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;

namespace Dhole.Pricing.Application.Features.Imports.GetPricingDecisionDashboard;

public sealed class GetPricingDecisionDashboardQueryHandler(IImportFclRateRepository importRates)
    : IQueryHandler<GetPricingDecisionDashboardQuery, Result<PricingDecisionDashboardDto>>
{
    public async Task<Result<PricingDecisionDashboardDto>> HandleAsync(
        GetPricingDecisionDashboardQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var dashboard = await importRates.GetDecisionDashboardAsync(
            query.DateFrom,
            query.DateTo,
            cancellationToken
        );

        return Result.Success(dashboard);
    }
}
