namespace Dhole.Pricing.Application.Abstractions.Mongo;

public interface IImportFclRateChangeSnapshotWriter
{
    Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        Guid importBatchId,
        string sourceType,
        string pol,
        string pod,
        string carrier,
        string containerType,
        string currency,
        decimal freight,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        string status,
        string? rawDataJson,
        string? sourceUrl,
        int usedAsRateCount,
        Guid? createdAsRateHeaderId,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    );
}
