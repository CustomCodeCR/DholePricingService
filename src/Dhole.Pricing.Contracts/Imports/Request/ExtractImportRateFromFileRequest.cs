namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record ImportRatesFromExtractionRequest(
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string SourceType,
    string FromAddress,
    string Subject,
    string OriginalFileName,
    decimal ConfidenceScore,
    ExtractedPricingDataRequest Response,
    string? ContentSourceType = null
);

public sealed record ExtractedPricingDataRequest(
    bool Success,
    Guid? ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    ExtractedPricingSummaryRequest Summary,
    IReadOnlyCollection<ExtractedPricingRowRequest> Rows,
    IReadOnlyCollection<ExtractedPricingIssueRequest> Issues,
    string? ErrorCode,
    string? ErrorMessage,
    ExtractedCatalogReferenceRequest? ProfileReference = null
);

public sealed record ExtractedPricingSummaryRequest(
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    bool HasIssues
);

public sealed record ExtractedPricingRowRequest(
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
    ExtractedCatalogReferenceRequest? OriginPortReference = null,
    ExtractedCatalogReferenceRequest? PortOfExitReference = null,
    ExtractedCatalogReferenceRequest? DestinationPortReference = null,
    ExtractedCatalogReferenceRequest? ContainerTypeReference = null,
    ExtractedCatalogReferenceRequest? CarrierReference = null,
    ExtractedCatalogReferenceRequest? AgentReference = null,
    ExtractedCatalogReferenceRequest? CurrencyReference = null
);

public sealed record ExtractedPricingIssueRequest(
    Guid Id,
    Guid? ExtractedPricingRowId,
    string Code,
    string Message,
    bool IsBlocking,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? ColumnName,
    string? RawValue
);

public sealed record ExtractedCatalogReferenceRequest(
    Guid Id,
    string CatalogGroupSlug,
    string Code,
    string Slug,
    string Name,
    string? RawValue
);

public sealed record ImportRatesFromExtractionResponse(
    Guid PricingImportBatchId,
    Guid? ExtractionExecutionId,
    int PersistedRows,
    int SkippedRows,
    IReadOnlyCollection<string> IssueCodes
);
