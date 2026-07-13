using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.Pricing.Infrastructure.Mongo.Documents;

[MongoCollectionName("pricing_cost_change_snapshots")]
public sealed class CostChangeSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string EventId { get; init; } = default!;

    public string EventName { get; init; } = default!;

    public string EntityType { get; init; } = default!;

    public string EntityId { get; init; } = default!;

    public string CostName { get; init; } = default!;

    public string CostType { get; init; } = default!;

    public string CostDetailType { get; init; } = default!;

    public string? CarrierId { get; init; }

    public string? CarrierNameSnapshot { get; init; }

    public string? CarrierCodeSnapshot { get; init; }

    public string? AgentId { get; init; }

    public string? AgentNameSnapshot { get; init; }

    public string? AgentCodeSnapshot { get; init; }

    public string PortId { get; init; } = default!;

    public string PortNameSnapshot { get; init; } = default!;

    public string PortCodeSnapshot { get; init; } = default!;

    public string PortRole { get; init; } = default!;

    public string CurrencyId { get; init; } = default!;

    public string CurrencyNameSnapshot { get; init; } = default!;

    public string CurrencyCodeSnapshot { get; init; } = default!;

    public decimal CostAmount { get; init; }

    public decimal SaleAmount { get; init; }

    public decimal UtilityAmount { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; }

    public string Action { get; init; } = default!;

    public string? PreviousValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? ChangedBy { get; init; }

    public DateTime ChangedAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholePricingService";
}
