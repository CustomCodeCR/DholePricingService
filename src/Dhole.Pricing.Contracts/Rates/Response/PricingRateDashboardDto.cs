namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record PricingRateDashboardDto(
    int TotalRates,
    int PendingApprovalCount,
    int ApprovedCount,
    int RejectedCount,
    int OpenCount,
    int SentCount,
    int RequestedByClientCount,
    int AcceptedByClientCount,
    int ClosedCount,
    int ExpiredCount,
    DateTime? LastCreatedAtUtc,
    DateTime? LastModifiedAtUtc,
    IReadOnlyCollection<PricingRateStatusSummaryDto> Statuses,
    IReadOnlyCollection<PricingRateCurrencySummaryDto> Financials,
    IReadOnlyCollection<PricingRateDashboardItemDto> RecentRates
);

public sealed record PricingRateStatusSummaryDto(
    string Status,
    int Count,
    decimal Percentage
);

public sealed record PricingRateCurrencySummaryDto(
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    int RateCount,
    decimal TotalCostAmount,
    decimal TotalSaleAmount,
    decimal TotalUtilityAmount,
    decimal AverageMarginPercentage
);

public sealed record PricingRateDashboardItemDto(
    Guid Id,
    string RateCode,
    string RateName,
    string Status,
    string? ClientName,
    string? CarrierName,
    string PolName,
    string PoeName,
    string PodName,
    string ContainerTypeName,
    string CurrencyCode,
    decimal TotalUtilityAmount,
    decimal MarginPercentage,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime ValidFrom,
    DateTime ValidTo
);
