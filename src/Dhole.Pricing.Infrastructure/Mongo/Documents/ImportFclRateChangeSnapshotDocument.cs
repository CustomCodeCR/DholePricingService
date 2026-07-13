using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.Pricing.Infrastructure.Mongo.Documents;

[MongoCollectionName("pricing_import_fcl_rate_change_snapshots")]
public sealed class ImportFclRateChangeSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string EventId { get; init; } = default!;

    public string EventName { get; init; } = default!;

    public string EntityType { get; init; } = default!;

    public string EntityId { get; init; } = default!;

    public string ImportBatchId { get; init; } = default!;

    public string SourceType { get; init; } = default!;

    public string Pol { get; init; } = default!;

    public string Pod { get; init; } = default!;

    public string Carrier { get; init; } = default!;

    public string ContainerType { get; init; } = default!;

    public string Currency { get; init; } = default!;

    public decimal Freight { get; init; }

    public int FreeDays { get; init; }

    public DateTime ValidFrom { get; init; }

    public DateTime ValidTo { get; init; }

    public string Status { get; init; } = default!;

    public string? RawDataJson { get; init; }

    public string? SourceUrl { get; init; }

    public int UsedAsRateCount { get; init; }

    public string? CreatedAsRateHeaderId { get; init; }

    public string Action { get; init; } = default!;

    public string? PreviousValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? ChangedBy { get; init; }

    public DateTime ChangedAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholePricingService";
}
