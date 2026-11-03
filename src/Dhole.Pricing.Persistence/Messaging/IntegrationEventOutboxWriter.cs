using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Persistence.DbContexts;

namespace Dhole.Pricing.Persistence.Messaging;

public sealed class IntegrationEventOutboxWriter(ServiceDbContext dbContext)
    : IIntegrationEventOutboxWriter
{
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
        var payloadJson = JsonSerializer.Serialize(payload);
        if (string.Equals(eventType, "notifications.notification.requested", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            using var json = JsonDocument.Parse(payloadJson);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in json.RootElement.EnumerateObject())
                    values[property.Name] = property.Value.Clone();
                values["idempotencyKey"] = correlationId.Trim();
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
            CorrelationId = correlationId,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };

        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);
    }
}
