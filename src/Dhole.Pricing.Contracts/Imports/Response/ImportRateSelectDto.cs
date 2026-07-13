namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record ImportRateSelectDto(
    Guid Id,
    Guid ImportBatchId,
    string SourceType,
    string Pol,
    string Pod,
    string Carrier,
    string ContainerType,
    string Currency,
    decimal Freight,
    int FreeDays,
    DateTime validFrom,
    DateTime validTo,
    string RawDataJson,
    string Status,
    int UsedAsRateCount
);
