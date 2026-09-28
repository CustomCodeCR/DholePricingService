using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.ApplyAutoPricing;

public sealed record ApplyAutoPricingCommand(
    Guid RateId,
    Guid DecisionId,
    string? ProfileCode,
    DateTime? ReferenceDate,
    decimal? MinimumMarginPercentage,
    MarketBenchmarkAmountKind AmountKind,
    Guid? ContainerTypeId,
    Guid? ActorUserId,
    string? ActorUserName
) : ICommand<Result<AutoPricingApplyDto>>;

public sealed class ApplyAutoPricingCommandHandler(
    IMarketPricingApiWorkflow workflow
) : ICommandHandler<ApplyAutoPricingCommand, Result<AutoPricingApplyDto>>
{
    public async Task<Result<AutoPricingApplyDto>> HandleAsync(
        ApplyAutoPricingCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.ApplyAsync(
                command.RateId,
                command.DecisionId,
                command.ProfileCode,
                command.ReferenceDate,
                command.MinimumMarginPercentage,
                command.AmountKind,
                command.ContainerTypeId,
                command.ActorUserId,
                command.ActorUserName,
                cancellationToken
            );
            return Result.Success(result);
        }
        catch (MarketPricingContextException exception)
        {
            return Result.Failure<AutoPricingApplyDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AutoPricingApplyDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
