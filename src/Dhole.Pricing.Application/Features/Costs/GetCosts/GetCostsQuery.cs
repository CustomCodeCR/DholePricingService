using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Features.Costs.GetCosts;

public sealed record GetCostsQuery(
    PageRequest Page,
    string? Search = null,
    IReadOnlyCollection<CostType>? CostTypes = null,
    IReadOnlyCollection<CostDetailType>? CostDetailTypes = null,
    IReadOnlyCollection<Guid>? CarrierIds = null,
    IReadOnlyCollection<Guid>? AgentIds = null,
    IReadOnlyCollection<Guid>? PortIds = null,
    IReadOnlyCollection<CostPortRole>? PortRoles = null,
    IReadOnlyCollection<Guid>? CurrencyIds = null,
    IReadOnlyCollection<bool>? ActiveStates = null
) : IQuery<Result<PagedResult<CostDto>>>;
