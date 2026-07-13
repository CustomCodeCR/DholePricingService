using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Costs.Response;

namespace Dhole.Pricing.Application.Features.Costs.GetCostById;

public sealed record GetCostByIdQuery(Guid Id) : IQuery<Result<CostDto>>;
