using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    private async Task<string> ResolveConsolidationNameAsync(string payloadJson, CancellationToken ct)
    {
        if (JsonNode.Parse(payloadJson) is not JsonObject payload ||
            !string.Equals(payload["shipmentMode"]?.ToString(), "Lcl", StringComparison.OrdinalIgnoreCase))
            return payloadJson;
        var byId = Guid.TryParse(payload["consolidationId"]?.ToString(), out var id) && id != Guid.Empty;
        var byNumber = int.TryParse(payload["consolidationNumber"]?.ToString(), out var number) && number > 0;
        if (!byId && !byNumber) return payloadJson;
        var connection = dbContext.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = byId
                ? "SELECT id, consolidation_number, name FROM pricing.\"OwnLclConsolidations\" WHERE id = @identity"
                : "SELECT id, consolidation_number, name FROM pricing.\"OwnLclConsolidations\" WHERE consolidation_number = @identity";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "identity";
            parameter.Value = byId ? (object)id : number;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return payloadJson;
            var resolvedId = reader.GetGuid(0);
            var resolvedNumber = reader.GetInt32(1);
            var name = reader.GetString(2);
            if (await reader.ReadAsync(ct)) return payloadJson; // Ambiguous legacy number.
            payload["consolidationId"] = resolvedId.ToString("D");
            payload["consolidationNumber"] = resolvedNumber;
            payload["consolidationReference"] = name;
            return payload.ToJsonString();
        }
        finally { if (!wasOpen) await connection.CloseAsync(); }
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
        if (eventName is "pricing.quote.accepted.v1" or "pricing.quote.linked.v1")
            payloadJson = await ResolveConsolidationNameAsync(payloadJson, cancellationToken);
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
