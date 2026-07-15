using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Imports.Response;

namespace Dhole.Pricing.Application.Features.Imports.GetPricingDecisionDashboard;

public sealed record GetPricingDecisionDashboardQuery(
    DateTime? DateFrom,
    DateTime? DateTo,
    string? ContainerType
) : IQuery<Result<PricingDecisionDashboardDto>>;
