namespace Dhole.Pricing.Contracts.Imports.Response;

public sealed record ExtractImportFclRatesResultDto(
    bool Success,
    Guid PricingImportId,
    string CorrelationId,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    int CreatedRows,
    int SkippedRows,
    bool HasIssues,
    IReadOnlyCollection<Guid> ImportFclRateIds,
    IReadOnlyCollection<ExtractImportFclRatesIssueDto> Issues,
    string? ErrorCode,
    string? ErrorMessage
);

public sealed record ExtractImportFclRatesIssueDto(
    string Code,
    string Message,
    bool IsBlocking,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? ColumnName,
    string? RawValue
);
