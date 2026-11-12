using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Persistence.DbContexts;

namespace Dhole.Pricing.Persistence.Messaging;

public sealed class IntegrationEventOutboxWriter(ServiceDbContext dbContext)
    : IIntegrationEventOutboxWriter
{
    // OutboxMessageConfiguration maps correlation_id to varchar(100).
    // A long manual resend key previously caused a PostgreSQL 22001 failure.
    public const int MaximumCorrelationIdLength = 100;

    public static string? NormalizeCorrelationId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return null;

        var normalized = correlationId.Trim();
        if (normalized.Length <= MaximumCorrelationIdLength)
            return normalized;

        // Hash the ENTIRE original operation identity deterministically.
        // The same key must reach the database and Notifications payload to
        // preserve idempotency across retries, restarts and replays.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task WriteAsync(
        string eventType,
        string eventName,
        object payload,
        string? correlationId = null,
        CancellationToken cancellationToken = default
    )
    {
        // Carry the business operation key all the way to the Notifications stream.
        // An Outbox event may be published more than once (or recreated with a new
        // EventId); consumers must not interpret those deliveries as new emails.
        var normalizedCorrelationId = NormalizeCorrelationId(correlationId);
        var payloadJson = JsonSerializer.Serialize(payload);
        if (string.Equals(eventType, "notifications.notification.requested", StringComparison.Ordinal)
            && normalizedCorrelationId is not null)
        {
            using var json = JsonDocument.Parse(payloadJson);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in json.RootElement.EnumerateObject())
                    values[property.Name] = property.Value.Clone();
                values["idempotencyKey"] = normalizedCorrelationId;
                payloadJson = JsonSerializer.Serialize(values);
            }
        }

        var message = new OutboxMessage
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            EventName = eventName,
            SourceService = "DholePricingService",
            PayloadJson = payloadJson,
            HeadersJson = null,
            CorrelationId = normalizedCorrelationId,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };

        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);
    }
}
