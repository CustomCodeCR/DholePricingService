using CustomCodeFramework.Mongo.Abstractions;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Infrastructure.Mongo.Documents;

namespace Dhole.Pricing.Infrastructure.Mongo;

public sealed class RateDetailChangeSnapshotWriter(IMongoContext mongoContext)
    : IRateDetailChangeSnapshotWriter
{
    public Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        Guid rateHeaderId,
        Guid? costId,
        string name,
        string costDetailType,
        string costType,
        Guid currencyId,
        string currencyNameSnapshot,
        string currencyCodeSnapshot,
        decimal costAmount,
        decimal saleAmount,
        decimal utilityAmount,
        string? notes,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    )
    {
        var document = new RateDetailChangeSnapshotDocument
        {
            EventId = eventId.ToString(),
            EventName = eventName,
            EntityType = entityType,
            EntityId = entityId.ToString(),

            RateHeaderId = rateHeaderId.ToString(),
            CostId = costId?.ToString(),

            Name = name,
            CostDetailType = costDetailType,
            CostType = costType,

            CurrencyId = currencyId.ToString(),
            CurrencyNameSnapshot = currencyNameSnapshot,
            CurrencyCodeSnapshot = currencyCodeSnapshot,

            CostAmount = costAmount,
            SaleAmount = saleAmount,
            UtilityAmount = utilityAmount,
            Notes = notes,

            Action = action,
            PreviousValueJson = previousValueJson,
            NewValueJson = newValueJson,

            ChangedBy = changedBy?.ToString(),
            ChangedAtUtc = changedAtUtc,
            CorrelationId = correlationId?.ToString(),
        };

        return mongoContext
            .GetCollection<RateDetailChangeSnapshotDocument>()
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
