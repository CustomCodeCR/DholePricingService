using CustomCodeFramework.Mongo.Abstractions;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Infrastructure.Mongo.Documents;

namespace Dhole.Pricing.Infrastructure.Mongo;

public sealed class RateHeaderChangeSnapshotWriter(IMongoContext mongoContext)
    : IRateHeaderChangeSnapshotWriter
{
    public Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        Guid? sourceImportFclRateId,
        Guid agentId,
        string agentNameSnapshot,
        string agentCodeSnapshot,
        Guid carrierId,
        string carrierNameSnapshot,
        string carrierCodeSnapshot,
        Guid polId,
        string polNameSnapshot,
        string polCodeSnapshot,
        Guid poeId,
        string poeNameSnapshot,
        string poeCodeSnapshot,
        Guid podId,
        string podNameSnapshot,
        string podCodeSnapshot,
        Guid containerTypeId,
        string containerTypeNameSnapshot,
        string containerTypeCodeSnapshot,
        Guid currencyId,
        string currencyNameSnapshot,
        string currencyCodeSnapshot,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        decimal totalCostAmount,
        decimal totalSaleAmount,
        decimal totalUtilityAmount,
        decimal marginPercentage,
        bool requiredApproval,
        string status,
        string? rateDetailsJson,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    )
    {
        var document = new RateHeaderChangeSnapshotDocument
        {
            EventId = eventId.ToString(),
            EventName = eventName,
            EntityType = entityType,
            EntityId = entityId.ToString(),

            SourceImportFclRateId = sourceImportFclRateId?.ToString(),

            AgentId = agentId.ToString(),
            AgentNameSnapshot = agentNameSnapshot,
            AgentCodeSnapshot = agentCodeSnapshot,

            CarrierId = carrierId.ToString(),
            CarrierNameSnapshot = carrierNameSnapshot,
            CarrierCodeSnapshot = carrierCodeSnapshot,

            PolId = polId.ToString(),
            PolNameSnapshot = polNameSnapshot,
            PolCodeSnapshot = polCodeSnapshot,

            PoeId = poeId.ToString(),
            PoeNameSnapshot = poeNameSnapshot,
            PoeCodeSnapshot = poeCodeSnapshot,

            PodId = podId.ToString(),
            PodNameSnapshot = podNameSnapshot,
            PodCodeSnapshot = podCodeSnapshot,

            ContainerTypeId = containerTypeId.ToString(),
            ContainerTypeNameSnapshot = containerTypeNameSnapshot,
            ContainerTypeCodeSnapshot = containerTypeCodeSnapshot,

            CurrencyId = currencyId.ToString(),
            CurrencyNameSnapshot = currencyNameSnapshot,
            CurrencyCodeSnapshot = currencyCodeSnapshot,

            FreeDays = freeDays,
            ValidFrom = validFrom,
            ValidTo = validTo,

            TotalCostAmount = totalCostAmount,
            TotalSaleAmount = totalSaleAmount,
            TotalUtilityAmount = totalUtilityAmount,
            MarginPercentage = marginPercentage,
            RequiredApproval = requiredApproval,

            Status = status,
            RateDetailsJson = rateDetailsJson,

            Action = action,
            PreviousValueJson = previousValueJson,
            NewValueJson = newValueJson,

            ChangedBy = changedBy?.ToString(),
            ChangedAtUtc = changedAtUtc,
            CorrelationId = correlationId?.ToString(),
        };

        return mongoContext
            .GetCollection<RateHeaderChangeSnapshotDocument>()
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
