using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRateById;

public sealed record GetRateByIdQuery(Guid Id) : IQuery<Result<RateDto>>;
