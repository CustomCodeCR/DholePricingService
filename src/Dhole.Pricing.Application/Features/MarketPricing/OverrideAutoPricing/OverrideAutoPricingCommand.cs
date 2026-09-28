using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.OverrideAutoPricing;

public sealed record OverrideAutoPricingDetailCommandItem(
    Guid RateDetailId,
    decimal SaleAmount
);

public sealed record OverrideAutoPricingCommand(
    Guid RateId,
    Guid DecisionId,
    string Reason,
    IReadOnlyCollection<OverrideAutoPricingDetailCommandItem> Details,
    Guid ActorUserId,
    string? ActorUserName
) : ICommand<Result<AutoPricingOverrideDto>>;

public sealed class OverrideAutoPricingCommandHandler(
    IMarketPricingApiWorkflow workflow
) : ICommandHandler<OverrideAutoPricingCommand, Result<AutoPricingOverrideDto>>
{
    public async Task<Result<AutoPricingOverrideDto>> HandleAsync(
        OverrideAutoPricingCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.OverrideAsync(
                command.RateId,
                command.DecisionId,
                command.Reason,
                command.Details.Select(x => (x.RateDetailId, x.SaleAmount)).ToArray(),
                command.ActorUserId,
                command.ActorUserName,
                cancellationToken
            );
            return Result.Success(result);
        }
        catch (MarketPricingContextException exception)
        {
            return Result.Failure<AutoPricingOverrideDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AutoPricingOverrideDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
