using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Features.Costs.GetCosts;

public sealed record GetCostsQuery(
    PageRequest Page,
    string? Search = null,
    CostType? CostType = null,
    CostDetailType? CostDetailType = null,
    Guid? CarrierId = null,
    Guid? AgentId = null,
    Guid? PortId = null,
    CostPortRole? PortRole = null,
    Guid? CurrencyId = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<CostDto>>>;
