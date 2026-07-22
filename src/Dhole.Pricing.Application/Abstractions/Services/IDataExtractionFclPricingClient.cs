namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IDataExtractionFclPricingClient
{
    Task<DataExtractionFclPricingResult> ExtractAsync(
        DataExtractionFclPricingRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed record DataExtractionFclPricingRequest(
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string? ProfileCode,
    Guid? RequestedBy,
    string? RequestedByName,
    byte[] FileContent
);

public sealed record DataExtractionCatalogReference(
    Guid Id,
    string CatalogGroupSlug,
    string Code,
    string Slug,
    string Name,
    string? RawValue
);

public sealed record DataExtractionFclPricingResult(
    bool Success,
    Guid? ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    DataExtractionFclPricingSummary Summary,
    IReadOnlyCollection<DataExtractionFclPricingRow> Rows,
    IReadOnlyCollection<DataExtractionFclPricingIssue> Issues,
    string? ErrorCode,
    string? ErrorMessage,
    DataExtractionCatalogReference? ProfileReference = null
);

public sealed record DataExtractionFclPricingSummary(
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    bool HasIssues
);

public sealed record DataExtractionFclPricingRow(
    Guid Id,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? OriginPort,
    string? PortOfExit,
    string? DestinationPort,
    string? ContainerType,
    string? Carrier,
    string? Agent,
    string? Commodity,
    string? Currency,
    int? FreeDays,
    int? TransitDays,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    string? SpaceComment,
    string? Remarks,
    string Status,
    string? RawJson,
    DataExtractionCatalogReference? OriginPortReference,
    DataExtractionCatalogReference? PortOfExitReference,
    DataExtractionCatalogReference? DestinationPortReference,
    DataExtractionCatalogReference? ContainerTypeReference,
    DataExtractionCatalogReference? CarrierReference,
    DataExtractionCatalogReference? AgentReference,
    DataExtractionCatalogReference? CurrencyReference
)
{
    public bool HasAllRequiredCatalogReferences =>
        OriginPortReference is not null
        && PortOfExitReference is not null
        && DestinationPortReference is not null
        && ContainerTypeReference is not null
        && CarrierReference is not null
        && CurrencyReference is not null;
}

public sealed record DataExtractionFclPricingIssue(
    Guid Id,
    Guid? PricingExtractionRecordId,
    string Code,
    string Message,
    bool IsBlocking,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? ColumnName,
    string? RawValue
);
