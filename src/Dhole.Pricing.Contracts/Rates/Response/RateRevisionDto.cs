namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateRevisionDto(
    Guid Id,
    Guid RateHeaderId,
    int RevisionNumber,
    string Status,
    string RateName,
    string? IdtraNumber,
    string? QuoNumber,
    decimal TotalSaleUsd,
    decimal TotalSaleCrc,
    decimal MarginPercentage,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    string SnapshotJson
);
