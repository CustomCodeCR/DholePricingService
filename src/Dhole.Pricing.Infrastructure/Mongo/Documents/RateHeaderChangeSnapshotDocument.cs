using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.Pricing.Infrastructure.Mongo.Documents;

[MongoCollectionName("pricing_rate_header_change_snapshots")]
public sealed class RateHeaderChangeSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string EventId { get; init; } = default!;

    public string EventName { get; init; } = default!;

    public string EntityType { get; init; } = default!;

    public string EntityId { get; init; } = default!;

    public string? SourceImportFclRateId { get; init; }

    public string AgentId { get; init; } = default!;

    public string AgentNameSnapshot { get; init; } = default!;

    public string AgentCodeSnapshot { get; init; } = default!;

    public string CarrierId { get; init; } = default!;

    public string CarrierNameSnapshot { get; init; } = default!;

    public string CarrierCodeSnapshot { get; init; } = default!;

    public string PolId { get; init; } = default!;

    public string PolNameSnapshot { get; init; } = default!;

    public string PolCodeSnapshot { get; init; } = default!;

    public string PoeId { get; init; } = default!;

    public string PoeNameSnapshot { get; init; } = default!;

    public string PoeCodeSnapshot { get; init; } = default!;

    public string PodId { get; init; } = default!;

    public string PodNameSnapshot { get; init; } = default!;

    public string PodCodeSnapshot { get; init; } = default!;

    public string ContainerTypeId { get; init; } = default!;

    public string ContainerTypeNameSnapshot { get; init; } = default!;

    public string ContainerTypeCodeSnapshot { get; init; } = default!;

    public string CurrencyId { get; init; } = default!;

    public string CurrencyNameSnapshot { get; init; } = default!;

    public string CurrencyCodeSnapshot { get; init; } = default!;

    public int FreeDays { get; init; }

    public DateTime ValidFrom { get; init; }

    public DateTime ValidTo { get; init; }

    public decimal TotalCostAmount { get; init; }

    public decimal TotalSaleAmount { get; init; }

    public decimal TotalUtilityAmount { get; init; }

    public decimal MarginPercentage { get; init; }

    public bool RequiredApproval { get; init; }

    public string Status { get; init; } = default!;

    public string? RateDetailsJson { get; init; }

    public string Action { get; init; } = default!;

    public string? PreviousValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? ChangedBy { get; init; }

    public DateTime ChangedAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholePricingService";
}
