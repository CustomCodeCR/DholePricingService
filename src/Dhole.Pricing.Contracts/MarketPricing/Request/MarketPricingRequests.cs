namespace Dhole.Pricing.Contracts.MarketPricing.Request;

public sealed record CalculateMarketBenchmarkRequest(
    Guid IncotermId,
    Guid PolId,
    Guid? PoeId,
    Guid? PodId,
    Guid? ContainerTypeId,
    string Mode,
    Guid? CarrierId,
    DateTime? ReferenceDate = null,
    string? AmountKind = null,
    decimal? TargetPercentile = null,
    decimal? CompetitiveCeilingPercentile = null
);

public sealed record CalculateAutoPricingRequest(
    string? ProfileCode = null,
    DateTime? ReferenceDate = null,
    decimal? MinimumMarginPercentage = null,
    string? BenchmarkAmountKind = null,
    Guid? ContainerTypeId = null
);

public sealed record ApplyAutoPricingRequest(
    Guid DecisionId,
    string? ProfileCode = null,
    DateTime? ReferenceDate = null,
    decimal? MinimumMarginPercentage = null,
    string? BenchmarkAmountKind = null,
    Guid? ContainerTypeId = null
);

public sealed record AutoPricingOverrideDetailRequest(
    Guid RateDetailId,
    decimal SaleAmount
);

public sealed record OverrideAutoPricingRequest(
    Guid DecisionId,
    string Reason,
    IReadOnlyCollection<AutoPricingOverrideDetailRequest> Details
);

public sealed record ApproveAutoPricingRequest(Guid DecisionId);
