using Dhole.Pricing.Application.MarketPricing.Benchmark;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.Abstractions.MarketPricing;

public interface IMarketPricingApiWorkflow
{
    Task<MarketBenchmarkDto> CalculateBenchmarkAsync(
        MarketComparisonKey key,
        DateTime referenceDate,
        MarketBenchmarkAmountKind amountKind,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        CancellationToken cancellationToken = default
    );

    Task<MarketBenchmarkDto> CalculateRateBenchmarkAsync(
        Guid rateId,
        DateTime? referenceDate,
        Guid? containerTypeId,
        MarketBenchmarkAmountKind amountKind,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        CancellationToken cancellationToken = default
    );

    Task<AutoPricingCalculationDto> CalculateAndPersistAsync(
        Guid rateId,
        string? profileCode,
        DateTime? referenceDate,
        decimal? minimumMarginPercentage,
        MarketBenchmarkAmountKind amountKind,
        Guid? containerTypeId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default
    );

    Task<AutoPricingDecisionDto> GetLatestDecisionAsync(
        Guid rateId,
        CancellationToken cancellationToken = default
    );

    Task<AutoPricingApplyDto> ApplyAsync(
        Guid rateId,
        Guid decisionId,
        string? profileCode,
        DateTime? referenceDate,
        decimal? minimumMarginPercentage,
        MarketBenchmarkAmountKind amountKind,
        Guid? containerTypeId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default
    );

    Task<AutoPricingOverrideDto> OverrideAsync(
        Guid rateId,
        Guid decisionId,
        string reason,
        IReadOnlyCollection<(Guid RateDetailId, decimal SaleAmount)> details,
        Guid actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default
    );

    Task<AutoPricingApprovalDto> ApproveAsync(
        Guid rateId,
        Guid decisionId,
        Guid actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default
    );
}
