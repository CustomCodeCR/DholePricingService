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
    DateTime ValidFrom,
    DateTime ValidTo,
    string RawDataJson,
    string Status,
    int UsedAsRateCount,
    Guid? PolId = null,
    Guid? PoeId = null,
    string Poe = "",
    Guid? PodId = null,
    Guid? CarrierId = null,
    Guid? ContainerTypeId = null,
    string ContainerTypeCode = "",
    Guid? CurrencyId = null,
    decimal? TotalSale = null,
    int? TransitDays = null
);