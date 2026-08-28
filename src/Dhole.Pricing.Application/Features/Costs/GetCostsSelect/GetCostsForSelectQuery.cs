using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;

public sealed record GetCostsForSelectQuery(
    string? Search = null,
    CostType? CostType = null,
    CostDetailType? CostDetailType = null,
    Guid? CarrierId = null,
    Guid? AgentId = null,
    Guid? PortId = null,
    CostPortRole? PortRole = null,
    Guid? CurrencyId = null,
    bool? IsActive = true,
    Guid? PolId = null,
    Guid? PoeId = null,
    Guid? PodId = null,
    Guid? IncotermId = null,
    ShipmentMode? ShipmentMode = null,
    bool ApplicableToContext = false,
    IReadOnlyCollection<Guid>? ServiceIds = null
) : IQuery<Result<IReadOnlyCollection<CostSelectDto>>>;
