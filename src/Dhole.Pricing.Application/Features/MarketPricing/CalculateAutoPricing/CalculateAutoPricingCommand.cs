using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.CalculateAutoPricing;

public sealed record CalculateAutoPricingCommand(
    Guid RateId,
    string? ProfileCode,
    DateTime? ReferenceDate,
    decimal? MinimumMarginPercentage,
    MarketBenchmarkAmountKind AmountKind,
    Guid? ContainerTypeId,
    Guid? ActorUserId,
    string? ActorUserName
) : ICommand<Result<AutoPricingCalculationDto>>;

public sealed class CalculateAutoPricingCommandHandler(
    IMarketPricingApiWorkflow workflow
) : ICommandHandler<CalculateAutoPricingCommand, Result<AutoPricingCalculationDto>>
{
    public async Task<Result<AutoPricingCalculationDto>> HandleAsync(
        CalculateAutoPricingCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.CalculateAndPersistAsync(
                command.RateId,
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
            return Result.Failure<AutoPricingCalculationDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AutoPricingCalculationDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
