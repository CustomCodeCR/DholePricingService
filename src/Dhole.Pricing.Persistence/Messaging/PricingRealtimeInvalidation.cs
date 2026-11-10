using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Dhole.Pricing.Persistence.Messaging;

/// <summary>
/// Typed and PII-free F6.02 invalidations. They are queued in Pricing's
/// existing EF Outbox in the same SaveChanges transaction as RateHeader.
/// </summary>
public static class PricingRealtimeInvalidation
{
    public const string ReleaseId = "rt-20261107-01";
    public const string StreamName = "dhole.pricing.realtime.v1";
    public static readonly Guid CostListId = Guid.Parse("00000000-0000-4000-8000-000000000043");
    public static readonly Guid RateListId = Guid.Parse("00000000-0000-4000-8000-000000000044");
    private static long _lastHint;
    public static long NextVersionHint()
    {
        while (true)
        {
            var microsecond = (DateTimeOffset.UtcNow.Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10;
            var previous = Volatile.Read(ref _lastHint);
            var next = Math.Max(microsecond, previous + 1);
            if (Interlocked.CompareExchange(ref _lastHint, next, previous) == previous) return next;
        }
    }

    public static bool IsEnabled(IConfiguration? config) =>
        config?.GetValue<bool>("Features:UniversalRealtimeEnabled") == true &&
        config.GetValue<bool>("Realtime:Outbox:Enabled") &&
        config.GetValue<bool>("Realtime:BatchApproved") &&
        string.Equals(config["Realtime:ReleaseId"], ReleaseId, StringComparison.Ordinal);

    public static string EventType(string action) => action switch
    {
        "created" => "pricing.rate.created.v1",
        "updated" => "pricing.rate.updated.v1",
        "deleted" => "pricing.rate.deleted.v1",
        _ => throw new ArgumentException("Unsupported Pricing rate action.", nameof(action))
    };

    public static string CollectionEnvelope(Guid eventId, string collection, long version,
        DateTimeOffset occurredAt, string correlationId)
    {
        var aggregateId = collection switch
        {
            "cost-list" => CostListId,
            "rate-list" => RateListId,
            _ => throw new ArgumentException("Unsupported collection.", nameof(collection))
        };
        if (eventId == Guid.Empty || version <= 0 || occurredAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Invalid collection invalidation.");
        return JsonSerializer.Serialize(new
        {
            eventId,
            eventType = $"pricing.{collection}.changed.v1",
            schemaVersion = 1,
            aggregateType = collection,
            aggregateId = aggregateId.ToString("D"),
            aggregateVersion = version,
            occurredAtUtc = occurredAt,
            producer = "DholePricingService",
            correlationId,
            audienceRef = $"pricing:{collection}:{aggregateId:D}",
            payload = new { changedFields = new[] { "collection" } }
        });
    }

    public static string Envelope(
        Guid eventId, Guid rateId, int revision, string action,
        DateTimeOffset occurredAt, string correlationId)
    {
        if (eventId == Guid.Empty || rateId == Guid.Empty || revision < 1 ||
            occurredAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Invalid minimal Pricing realtime envelope.");
        var eventType = EventType(action);
        return JsonSerializer.Serialize(new
        {
            eventId,
            eventType,
            schemaVersion = 1,
            aggregateType = "rate",
            aggregateId = rateId.ToString("D"),
            aggregateVersion = revision,
            occurredAtUtc = occurredAt,
            producer = "DholePricingService",
            correlationId,
            audienceRef = $"pricing:rate:{rateId:D}",
            payload = new { changedFields = new[] { "rate" } }
        });
    }
}
