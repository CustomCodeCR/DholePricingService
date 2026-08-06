using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRateDashboard;

public sealed record GetRateDashboardQuery(
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null,
    DateTime? ModifiedFrom = null,
    DateTime? ModifiedTo = null,
    DateTime? ValidityFrom = null,
    DateTime? ValidityTo = null
) : IQuery<Result<PricingRateDashboardDto>>;
