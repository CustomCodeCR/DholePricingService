using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Api;
using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.MarketPricing.GetRateMarketBenchmark;

public sealed record GetRateMarketBenchmarkQuery(
    Guid RateId,
    DateTime? ReferenceDate,
    Guid? ContainerTypeId,
    MarketBenchmarkAmountKind AmountKind,
    decimal TargetPercentile,
    decimal CompetitiveCeilingPercentile
) : IQuery<Result<MarketBenchmarkDto>>;

public sealed class GetRateMarketBenchmarkQueryHandler(
    IMarketPricingApiWorkflow workflow
) : IQueryHandler<GetRateMarketBenchmarkQuery, Result<MarketBenchmarkDto>>
{
    public async Task<Result<MarketBenchmarkDto>> HandleAsync(
        GetRateMarketBenchmarkQuery query,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await workflow.CalculateRateBenchmarkAsync(
                query.RateId,
                query.ReferenceDate,
                query.ContainerTypeId,
                query.AmountKind,
                query.TargetPercentile,
                query.CompetitiveCeilingPercentile,
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
