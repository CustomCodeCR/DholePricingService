using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.CalculateMarketBenchmark;

public sealed record CalculateMarketBenchmarkCommand(
    MarketComparisonKey Key,
    DateTime ReferenceDate,
    MarketBenchmarkAmountKind AmountKind,
    decimal TargetPercentile,
    decimal CompetitiveCeilingPercentile
) : ICommand<Result<MarketBenchmarkDto>>;

public sealed class CalculateMarketBenchmarkCommandHandler(
    IMarketPricingApiWorkflow workflow
) : ICommandHandler<CalculateMarketBenchmarkCommand, Result<MarketBenchmarkDto>>
{
    public async Task<Result<MarketBenchmarkDto>> HandleAsync(
        CalculateMarketBenchmarkCommand command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.CalculateBenchmarkAsync(
                command.Key,
                command.ReferenceDate,
                command.AmountKind,
                command.TargetPercentile,
                command.CompetitiveCeilingPercentile,
                cancellationToken
            );
            return Result.Success(result);
        }
        catch (MarketPricingContextException exception)
        {
            return Result.Failure<MarketBenchmarkDto>(exception.Error);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<MarketBenchmarkDto>(
                PricingErrors.MarketPricingInvalidRequest(exception.Message)
            );
        }
    }
}
