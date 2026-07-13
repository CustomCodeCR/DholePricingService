namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record ImportRateDto(
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
    DateTime ValidFrom,
    DateTime ValidTo,
    string RawDataJson,
    string Status,
    int UsedAsRateCount
);
