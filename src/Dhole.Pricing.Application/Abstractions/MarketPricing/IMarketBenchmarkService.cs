using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IMarketBenchmarkService
{
    Task<MarketBenchmarkResult> CalculateAsync(
        MarketBenchmarkRequest request,
        CancellationToken cancellationToken = default
    );
}
