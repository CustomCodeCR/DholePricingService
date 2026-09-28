using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.GetAutoPricingDecision;

public sealed record GetAutoPricingDecisionQuery(Guid RateId)
    : IQuery<Result<AutoPricingDecisionDto>>;

public sealed class GetAutoPricingDecisionQueryHandler(
    IMarketPricingApiWorkflow workflow
) : IQueryHandler<GetAutoPricingDecisionQuery, Result<AutoPricingDecisionDto>>
{
    public async Task<Result<AutoPricingDecisionDto>> HandleAsync(
        GetAutoPricingDecisionQuery query,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.GetLatestDecisionAsync(
                query.RateId,
                cancellationToken
            );
            return Result.Success(result);
        }
        catch (MarketPricingContextException exception)
        {
            return Result.Failure<AutoPricingDecisionDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AutoPricingDecisionDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
