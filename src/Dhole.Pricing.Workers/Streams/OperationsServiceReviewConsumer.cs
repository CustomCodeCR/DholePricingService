using System.Text.Json;
using CustomCodeFramework.Messaging.Inbox;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Dhole.Pricing.Worker.Streams;

/// <summary>Durable Operations->Pricing service-review inbox, keyed by business request ID.</summary>
public sealed class OperationsServiceReviewConsumer(
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    IConfiguration configuration,
    ILogger<OperationsServiceReviewConsumer> logger) : BackgroundService
{
    private const string Stream = "dhole.pricing.operations.reviews";
    private const string Group = "dhole-pricing-operations-review";
    private const string Consumer = "pricing-operations-review-worker";
    private const string EventType = "operations.pricing.review.requested.v1";
    private const string InboxName = "pricing-operations-review-v1";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                try { await db.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); }
                catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
                var messages = await db.StreamReadGroupAsync(Stream, Group, Consumer, "0-0", count: 20);
                if (messages.Length == 0)
                    messages = await db.StreamReadGroupAsync(Stream, Group, Consumer, ">", count: 20);
                foreach (var message in messages)
                {
                    try
                    {
                        var fields = message.Values.ToDictionary(x => x.Name.ToString(),
                            x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                        if (fields.GetValueOrDefault("message_type") != EventType ||
                            fields.GetValueOrDefault("header:source_service") != "DholeOperationsService" ||
                            !Guid.TryParse(fields.GetValueOrDefault("header:event_id"), out var eventId) ||
                            !fields.TryGetValue("payload_json", out var json))
                            throw new InvalidDataException("Invalid Operations service review envelope.");
                        using var payload = JsonDocument.Parse(json);
                        await ProcessAsync(eventId, payload.RootElement, ct);
                        await db.StreamAcknowledgeAsync(Stream, Group, message.Id);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Operations review {RedisId} retained for retry", message.Id);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Operations review stream temporarily unavailable");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task ProcessAsync(Guid eventId, JsonElement data, CancellationToken ct)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !Guid.TryParse(Value(data, "reviewRequestId"), out var requestId) ||
            !Guid.TryParse(Value(data, "quoteId"), out var quoteId) ||
            !Guid.TryParse(Value(data, "caseId"), out var caseId) ||
            !Guid.TryParse(Value(data, "addedBy"), out var actor) ||
            data.TryGetProperty("schemaVersion", out var schema) == false ||
            schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1)
            throw new InvalidDataException("Operations review contract incomplete.");
        var code = Value(data, "serviceCode") ?? "";
        var name = Value(data, "serviceName") ?? "";
        var reason = Value(data, "reason") ?? "";
        if (requestId == Guid.Empty || quoteId == Guid.Empty || caseId == Guid.Empty ||
            actor == Guid.Empty || code.Length is < 1 or > 80 || name.Length is < 1 or > 300 ||
            reason.Length is < 1 or > 2000)
            throw new InvalidDataException("Operations review request invalid.");

        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var lockKey = "pricing-operations-service-review:" + requestId.ToString("N");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0));", ct);
        var handled = await context.InboxMessages.AnyAsync(x =>
            x.ConsumerService == InboxName && x.EventId == eventId, ct);
        if (!handled)
        {
            var existing = await context.OperationsServiceReviews.SingleOrDefaultAsync(x => x.Id == requestId, ct);
            if (existing is null)
            {
                var accepted = await context.RateHeaders.AsNoTracking().AnyAsync(x =>
                    x.Id == quoteId && x.Status == RateStatus.AcceptedByClient, ct);
                if (!accepted)
                    throw new InvalidOperationException("Accepted source QUO not found; review remains pending.");
                context.OperationsServiceReviews.Add(new PricingOperationsServiceReview
                {
                    Id = requestId, QuoteId = quoteId, CaseId = caseId,
                    Idtra = (Value(data, "idtra") ?? "")[..Math.Min(100, (Value(data, "idtra") ?? "").Length)],
                    ServiceCode = code, ServiceName = name, Reason = reason,
                    RequestedBy = actor,
                    RequestedAtUtc = data.TryGetProperty("addedAtUtc", out var created) &&
                        created.ValueKind == JsonValueKind.String &&
                        created.TryGetDateTimeOffset(out var createdAt)
                            ? createdAt : DateTimeOffset.UtcNow
                });
            }
            else if (existing.QuoteId != quoteId || existing.CaseId != caseId)
                throw new InvalidOperationException("Review business key collision.");
            context.InboxMessages.Add(new InboxMessage
            {
                ConsumerService = InboxName,
                SourceService = "DholeOperationsService",
                EventId = eventId,
                EventType = EventType,
                EventName = EventType,
                Status = InboxMessageStatus.Processed,
                CreatedAtUtc = DateTime.UtcNow,
                ProcessedAtUtc = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string? Value(JsonElement data, string key) =>
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
