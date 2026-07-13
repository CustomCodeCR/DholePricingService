using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.Pricing.Infrastructure.Mongo.Documents;

[MongoCollectionName("pricing_rate_detail_change_snapshots")]
public sealed class RateDetailChangeSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string EventId { get; init; } = default!;

    public string EventName { get; init; } = default!;

    public string EntityType { get; init; } = default!;

    public string EntityId { get; init; } = default!;

    public string RateHeaderId { get; init; } = default!;

    public string? CostId { get; init; }

    public string Name { get; init; } = default!;

    public string CostDetailType { get; init; } = default!;

    public string CostType { get; init; } = default!;

    public string CurrencyId { get; init; } = default!;

    public string CurrencyNameSnapshot { get; init; } = default!;

    public string CurrencyCodeSnapshot { get; init; } = default!;

    public decimal CostAmount { get; init; }

    public decimal SaleAmount { get; init; }

    public decimal UtilityAmount { get; init; }

    public string? Notes { get; init; }

    public string Action { get; init; } = default!;

    public string? PreviousValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? ChangedBy { get; init; }

    public DateTime ChangedAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholePricingService";
}
