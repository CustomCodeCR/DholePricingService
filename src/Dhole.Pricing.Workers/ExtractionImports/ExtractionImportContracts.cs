using Dhole.Pricing.Contracts.Imports.Request;

namespace Dhole.Pricing.Worker.ExtractionImports;

internal static class ExtractionImportMessageTypes
{
    public const string Requested = "pricing.import-from-extraction.requested";
    public const string Completed = "pricing.import-from-extraction.completed";
    public const string Failed = "pricing.import-from-extraction.failed";
}

internal sealed record PricingImportFromExtractionRequestedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string SourceType,
    string FromAddress,
    string Subject,
    string OriginalFileName,
    decimal ConfidenceScore,
    string ContentSourceType,
    string CorrelationId,
    ExtractedPricingDataRequest Response,
    DateTime OccurredAtUtc
);

internal sealed record PricingImportFromExtractionCompletedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportBatchId,
    int PersistedRows,
    int SkippedRows,
    string CorrelationId,
    DateTime OccurredAtUtc
);

internal sealed record PricingImportFromExtractionFailedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    int AttemptCount,
    string CorrelationId,
    DateTime OccurredAtUtc
);

internal sealed class PricingExtractionJobException(
    string errorCode,
    string message,
    bool isTransient,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;

    public bool IsTransient { get; } = isTransient;
}
