using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Persistence.DbContexts;

namespace Dhole.Pricing.Persistence.Auditing;

public sealed class PricingAuditService(ServiceDbContext dbContext) : IPricingAuditService
{
    private const string SourceService = "DholePricingService";
    private const string AuditEventType =
        "Dhole.AuditLogs.Contracts.AuditEvents.RegisterAuditEventRequest";
    private const string AuditEventName = "audit.event.registered";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(
        PricingAuditEvent auditEvent,
        CancellationToken cancellationToken = default
    )
    {
        var current = AuditExecutionContextAccessor.Current;
        var eventId = auditEvent.EventId ?? Guid.NewGuid();
        var correlationId = current?.CorrelationId ?? Guid.NewGuid();
        var occurredAt = auditEvent.OccurredAt ?? DateTime.UtcNow;

        var payload = new
        {
            EventId = eventId,
            CorrelationId = correlationId,
            SourceService,
            auditEvent.EntityType,
            auditEvent.EntityId,
            auditEvent.Action,
            auditEvent.EventType,
            UserId = auditEvent.ActorUserId ?? current?.UserId,
            UserName = auditEvent.ActorUserName ?? current?.UserName,
            IpAddress = current?.IpAddress,
            UserAgent = current?.UserAgent,
            OccurredAt = occurredAt,
            BeforeJson = SerializeNullable(auditEvent.Before),
            AfterJson = SerializeNullable(auditEvent.After),
            PayloadJson = SerializeNullable(auditEvent.Payload),
            Metadata = SerializeNullable(auditEvent.Metadata),
            auditEvent.ErrorMessage,
            StackTrace = (string?)null,
            Details = Array.Empty<object>(),
        };

        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                EventId = Guid.NewGuid(),
                EventType = AuditEventType,
                EventName = AuditEventName,
                SourceService = SourceService,
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                HeadersJson = null,
                CorrelationId = correlationId.ToString(),
                Status = OutboxMessageStatus.Pending,
                RetryCount = 0,
                ErrorMessage = null,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );

        // Same transactional Outbox as the original audit. Publish only genuine
        // business transitions; a replay, edit or repeated approval must not
        // trigger a new CRM command. This does NOT invoke Dynamics or enable rules.
        if (auditEvent.EntityType == PricingAuditEntityTypes.RateHeader &&
            auditEvent.EntityId is Guid rateId && rateId != Guid.Empty &&
            auditEvent.Before is not null && auditEvent.After is not null)
        {
            var before = JsonSerializer.SerializeToElement(auditEvent.Before, JsonOptions);
            var after = JsonSerializer.SerializeToElement(auditEvent.After, JsonOptions);
            static string? StringProp(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            static bool BoolProp(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                value.GetBoolean();

            string? integrationEvent = null;
            var acceptedNow = StringProp(after, "status") == "AcceptedByClient";
            var idtraAssignedAfterAcceptance =
                acceptedNow &&
                StringProp(before, "status") == "AcceptedByClient" &&
                string.IsNullOrWhiteSpace(StringProp(before, "idtraNumber")) &&
                !string.IsNullOrWhiteSpace(StringProp(after, "idtraNumber"));
            // A modified, already-accepted rate must refresh the SAME Dynamics
            // quote on its new commercial revision, even when IDTRA is unchanged.
            // Repeated audit events for the same revision must not enqueue writes.
            var acceptedRevisionUpdated =
                acceptedNow &&
                StringProp(before, "status") == "AcceptedByClient" &&
                !string.IsNullOrWhiteSpace(StringProp(after, "idtraNumber")) &&
                before.TryGetProperty("revisionNumber", out var previousRevision) &&
                previousRevision.TryGetInt32(out var previousNumber) &&
                after.TryGetProperty("revisionNumber", out var nextRevision) &&
                nextRevision.TryGetInt32(out var nextNumber) &&
                nextNumber > previousNumber;
            if (acceptedNow && StringProp(before, "status") != "AcceptedByClient")
                integrationEvent = "pricing.rate.accepted_by_client";
            else if (idtraAssignedAfterAcceptance)
                integrationEvent = "pricing.rate.idtra_assigned";
            else if (acceptedRevisionUpdated)
                integrationEvent = "pricing.rate.accepted_revision_updated";
            else if (auditEvent.EventType == PricingAuditEventTypes.RateHeaderApprovalChanged &&
                BoolProp(before,"requiredApproval") && !BoolProp(after,"requiredApproval"))
                integrationEvent = "pricing.rate.margin_approved";
            if (integrationEvent is not null)
            {
                var selected = new Dictionary<string, JsonElement>();
                foreach (var field in new[] { "rateCode", "rateName", "clientName",
                    "quoNumber", "idtraNumber", "currencyCode", "polName",
                    "podName", "poeName", "incotermCode", "totalSaleAmount",
                    "totalCostAmount", "validFrom", "validTo", "shipmentMode",
                    "executiveName" })
                {
                    if (after.TryGetProperty(field, out var value))
                        selected[field] = value.Clone();
                }
                var integrationEventId = Guid.NewGuid();
                dbContext.OutboxMessages.Add(new OutboxMessage
                {
                    EventId = integrationEventId,
                    EventType = integrationEvent,
                    EventName = integrationEvent,
                    SourceService = SourceService,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        eventId = integrationEventId,
                        rateId,
                        occurredAt,
                        fields = selected,
                    }, JsonOptions),
                    HeadersJson = null,
                    CorrelationId = correlationId.ToString(),
                    Status = OutboxMessageStatus.Pending,
                    RetryCount = 0,
                    ErrorMessage = null,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                // Queue the exact accepted revision in the same SaveChanges transaction as
                // the state transition. No HTTP call or credential is used here.
                if ((integrationEvent == "pricing.rate.accepted_by_client" ||
                     integrationEvent == "pricing.rate.idtra_assigned" ||
                     integrationEvent == "pricing.rate.accepted_revision_updated") &&
                    StringProp(after, "idtraNumber") is { Length: > 0 } idtra &&
                    !string.IsNullOrWhiteSpace(idtra) &&
                    after.TryGetProperty("revisionNumber", out var rev) &&
                    rev.TryGetInt32(out var revisionNumber) && revisionNumber > 0)
                {
                    dbContext.DynamicsQuoteSyncJobs.Add(new DynamicsQuoteSyncJob
                    {
                        Id = Guid.NewGuid(),
                        EventId = integrationEventId,
                        RateId = rateId,
                        RevisionNumber = revisionNumber,
                        PayloadJson = after.GetRawText(),
                        Status = "Pending",
                        NextAttemptAtUtc = DateTime.UtcNow,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        return Task.CompletedTask;
    }

    private static string? SerializeNullable(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value is string text ? text : JsonSerializer.Serialize(value, JsonOptions);
    }
}
