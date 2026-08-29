using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRateRevisions;

public sealed record GetRateRevisionsQuery(Guid RateHeaderId) : IQuery<Result<IReadOnlyCollection<RateRevisionDto>>>;
