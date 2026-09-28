using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.Benchmark;

public enum MarketBenchmarkAmountKind
{
    AllIn = 1,
    OceanFreight = 2,
    NormalizedAmount = 3,
}

public sealed record MarketBenchmarkRequest(
    MarketComparisonKey Key,
    DateTime ReferenceDate,
    MarketBenchmarkAmountKind AmountKind = MarketBenchmarkAmountKind.AllIn,
    decimal TargetPercentile = 60m,
    decimal CompetitiveCeilingPercentile = 65m
);
