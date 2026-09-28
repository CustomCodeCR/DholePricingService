using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.ApproveAutoPricing;

public sealed record ApproveAutoPricingCommand(
    Guid RateId,
    Guid DecisionId,
    Guid ActorUserId,
    string? ActorUserName
) : ICommand<Result<AutoPricingApprovalDto>>;

public sealed class ApproveAutoPricingCommandHandler(
    IMarketPricingApiWorkflow workflow
) : ICommandHandler<ApproveAutoPricingCommand, Result<AutoPricingApprovalDto>>
{
    public async Task<Result<AutoPricingApprovalDto>> HandleAsync(
        ApproveAutoPricingCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.ApproveAsync(
                command.RateId,
                command.DecisionId,
                command.ActorUserId,
                command.ActorUserName,
                cancellationToken
            );
            return Result.Success(result);
        }
        catch (MarketPricingContextException exception)
        {
            return Result.Failure<AutoPricingApprovalDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AutoPricingApprovalDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
