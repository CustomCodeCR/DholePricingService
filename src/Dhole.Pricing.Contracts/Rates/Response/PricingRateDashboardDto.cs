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
    IReadOnlyCollection<PricingRateDashboardItemDto> RecentRates,
    IReadOnlyCollection<PricingAcceptedModeSummaryDto> AcceptedByMode
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

// Los importes se agrupan por moneda; nunca se suman USD y CRC.
// Air incluye tanto Air como AirConsol para el reporte comercial.
public sealed record PricingAcceptedModeSummaryDto(
    string ShipmentMode,
    string CurrencyCode,
    string CurrencyName,
    int RateCount,
    decimal TotalCostAmount,
    decimal TotalSaleAmount,
    decimal TotalUtilityAmount
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
    string? PodName,
    string ContainerTypeName,
    string CurrencyCode,
    decimal TotalUtilityAmount,
    decimal MarginPercentage,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime ValidFrom,
    DateTime ValidTo
)
{
    public string? CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public string? CreatedByDisplayName { get; init; }
    public string? ReviewedByUserId { get; init; }
    public string? ReviewedByUserName { get; init; }
    public string? ReviewedByDisplayName { get; init; }
    public string? LastModifiedByUserId { get; init; }
    public string? LastModifiedByUserName { get; init; }
    public string? LastModifiedByDisplayName { get; init; }
}
