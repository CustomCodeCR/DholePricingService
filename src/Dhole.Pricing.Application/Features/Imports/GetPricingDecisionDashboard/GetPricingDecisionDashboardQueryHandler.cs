using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

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
            query.ContainerType,
            cancellationToken
        );

        var lanes = dashboard.Lanes
            .Select(lane =>
            {
                IReadOnlyCollection<PricingDecisionRateDto> rates = lane.Rates
                    .Where(rate => string.Equals(
                        rate.Status,
                        nameof(ImportStatus.Approved),
                        StringComparison.OrdinalIgnoreCase
                    ))
                    .ToArray();

                return lane with { TotalOptions = rates.Count, Rates = rates };
            })
            .ToArray();

        var approvedDashboard = dashboard with
        {
            TotalOptions = lanes.Sum(x => x.TotalOptions),
            Lanes = lanes,
        };

        return Result.Success(approvedDashboard);
    }
}
