namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record CreateImportRateRequest(
    Guid ImportBatchId,
    Guid ExtractionRecordId,
    string SourceType,
    ImportCatalogSnapshotRequest Profile,
    ImportCatalogSnapshotRequest Pol,
    ImportCatalogSnapshotRequest? Poe,
    ImportCatalogSnapshotRequest Pod,
    ImportCatalogSnapshotRequest Carrier,
    ImportCatalogSnapshotRequest Agent,
    ImportCatalogSnapshotRequest ContainerType,
    ImportCatalogSnapshotRequest Currency,
    string? Commodity,
    decimal OceanFreight,
    decimal OriginCharges,
    decimal DestinationCharges,
    decimal Surcharges,
    decimal TotalCost,
    decimal TotalSale,
    decimal Profit,
    decimal Margin,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? RawDataJson
);
