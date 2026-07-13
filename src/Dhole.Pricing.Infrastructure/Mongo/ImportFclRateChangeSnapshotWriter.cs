using CustomCodeFramework.Mongo.Abstractions;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Infrastructure.Mongo.Documents;

namespace Dhole.Pricing.Infrastructure.Mongo;

public sealed class ImportFclRateChangeSnapshotWriter(IMongoContext mongoContext)
    : IImportFclRateChangeSnapshotWriter
{
    public Task WriteAsync(
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
    )
    {
        var document = new ImportFclRateChangeSnapshotDocument
        {
            EventId = eventId.ToString(),
            EventName = eventName,
            EntityType = entityType,
            EntityId = entityId.ToString(),

            ImportBatchId = importBatchId.ToString(),
            SourceType = sourceType,

            Pol = pol,
            Pod = pod,
            Carrier = carrier,
            ContainerType = containerType,
            Currency = currency,

            Freight = freight,
            FreeDays = freeDays,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Status = status,

            RawDataJson = rawDataJson,
            SourceUrl = sourceUrl,

            UsedAsRateCount = usedAsRateCount,
            CreatedAsRateHeaderId = createdAsRateHeaderId?.ToString(),

            Action = action,
            PreviousValueJson = previousValueJson,
            NewValueJson = newValueJson,

            ChangedBy = changedBy?.ToString(),
            ChangedAtUtc = changedAtUtc,
            CorrelationId = correlationId?.ToString(),
        };

        return mongoContext
            .GetCollection<ImportFclRateChangeSnapshotDocument>()
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
