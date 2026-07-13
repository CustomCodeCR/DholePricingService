using CustomCodeFramework.Mongo.Abstractions;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Infrastructure.Mongo.Documents;

namespace Dhole.Pricing.Infrastructure.Mongo;

public sealed class CostChangeSnapshotWriter(IMongoContext mongoContext) : ICostChangeSnapshotWriter
{
    public Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        string costName,
        string costType,
        string costDetailType,
        Guid? carrierId,
        string? carrierNameSnapshot,
        string? carrierCodeSnapshot,
        Guid? agentId,
        string? agentNameSnapshot,
        string? agentCodeSnapshot,
        Guid portId,
        string portNameSnapshot,
        string portCodeSnapshot,
        string portRole,
        Guid currencyId,
        string currencyNameSnapshot,
        string currencyCodeSnapshot,
        decimal costAmount,
        decimal saleAmount,
        decimal utilityAmount,
        string? notes,
        bool isActive,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    )
    {
        var document = new CostChangeSnapshotDocument
        {
            EventId = eventId.ToString(),
            EventName = eventName,
            EntityType = entityType,
            EntityId = entityId.ToString(),

            CostName = costName,
            CostType = costType,
            CostDetailType = costDetailType,

            CarrierId = carrierId?.ToString(),
            CarrierNameSnapshot = carrierNameSnapshot,
            CarrierCodeSnapshot = carrierCodeSnapshot,

            AgentId = agentId?.ToString(),
            AgentNameSnapshot = agentNameSnapshot,
            AgentCodeSnapshot = agentCodeSnapshot,

            PortId = portId.ToString(),
            PortNameSnapshot = portNameSnapshot,
            PortCodeSnapshot = portCodeSnapshot,
            PortRole = portRole,

            CurrencyId = currencyId.ToString(),
            CurrencyNameSnapshot = currencyNameSnapshot,
            CurrencyCodeSnapshot = currencyCodeSnapshot,

            CostAmount = costAmount,
            SaleAmount = saleAmount,
            UtilityAmount = utilityAmount,
            Notes = notes,
            IsActive = isActive,

            Action = action,
            PreviousValueJson = previousValueJson,
            NewValueJson = newValueJson,

            ChangedBy = changedBy?.ToString(),
            ChangedAtUtc = changedAtUtc,
            CorrelationId = correlationId?.ToString(),
        };

        return mongoContext
            .GetCollection<CostChangeSnapshotDocument>()
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
