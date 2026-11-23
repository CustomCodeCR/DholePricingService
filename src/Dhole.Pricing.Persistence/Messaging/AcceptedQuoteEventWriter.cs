using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Messaging;

/// <summary>
/// A durable accepted-quotation contract. Event and RateHeader commit together;
/// never calls Operations synchronously or guesses a missing CRM identity.
/// </summary>
public sealed class AcceptedQuoteEventWriter(
    ServiceDbContext db,
    IIntegrationEventOutboxWriter outbox) : IAcceptedQuoteEventWriter
{
    public const string EventName = "pricing.quote.accepted.v1";

    public async Task QueueAsync(
        RateHeader rate, RateStatus previousStatus, string acceptedSnapshotJson,
        Guid? acceptedBy, string? observations, CancellationToken cancellationToken)
    {
        if (previousStatus == RateStatus.AcceptedByClient || rate.Status != RateStatus.AcceptedByClient)
            return;

        // TARIFARIO is not a commercial QUO. Internal margin approvals never reach this path.
        if (string.IsNullOrWhiteSpace(rate.QuoNumber) &&
            !rate.RateCode.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase))
            return;

        // Read the persisted A/B selection. An unselected rate must not create an IDTRA.
        var groupRows = await db.Database.SqlQueryRaw<string>(
            """
            SELECT a.parent_rate_id::text || '|' || a.base_quote || '|' || a.status AS "Value"
            FROM pricing.rate_quote_alternatives a
            WHERE a.rate_id = {0}
            """, rate.Id).ToArrayAsync(cancellationToken);
        Guid? groupId = null;
        string quoteNumber = rate.QuoNumber ?? rate.RateCode;
        if (groupRows.Length > 0)
        {
            var parts = groupRows[0].Split('|', 3);
            if (parts.Length != 3 || !Guid.TryParse(parts[0], out var parsedId) ||
                !string.Equals(parts[2], "Selected", StringComparison.Ordinal))
                throw new InvalidOperationException("La opción de la QUO debe estar seleccionada antes de generar la Apertura.");
            groupId = parsedId;
            quoteNumber = parts[1];
        }

        // When an old published revision was accepted while a newer draft was
        // open, use the PUBLISHED immutable snapshot rather than the new draft.
        var revisionNumber = rate.RevisionNumber;
        var snapshotJson = acceptedSnapshotJson;
        if (previousStatus == RateStatus.Open)
        {
            var lastPublished = await db.Set<RateRevision>().AsNoTracking()
                .Where(x => x.RateHeaderId == rate.Id &&
                    (x.Status == "Sent" || x.Status == "RequestedByClient" || x.Status == "Expired"))
                .OrderByDescending(x => x.RevisionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (lastPublished is not null &&
                lastPublished.RevisionNumber < rate.RevisionNumber)
            {
                revisionNumber = lastPublished.RevisionNumber;
                snapshotJson = lastPublished.SnapshotJson;
            }
        }

        using var document = JsonDocument.Parse(snapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("Details", out var details) ||
            details.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("La versión comercial aceptada no contiene un snapshot verificable.");

        var executiveId = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rate.Id && x.SellerUserId.HasValue)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => x.SellerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        // Persist the entire immutable snapshot: no future Pricing edits can
        // retroactively change what the customer accepted.
        await outbox.WriteAsync(EventName, EventName, new
        {
            schemaVersion = 1,
            quoteId = rate.Id,
            quoteGroupId = groupId,
            selectedOptionId = groupId.HasValue ? rate.Id : (Guid?)null,
            quoteNumber,
            revisionNumber,
            clientAccountId = rate.ClientAccountId,
            clientName = rate.ClientName,
            executiveUserId = executiveId,
            acceptedBy,
            acceptedAtUtc = rate.AcceptedAtUtc,
            acceptanceObservations = observations,
            commercialSnapshot = document.RootElement.Clone(),
        },
        correlationId: $"quote.accepted:{(groupId ?? rate.Id):N}:r{revisionNumber}",
        cancellationToken: cancellationToken);
    }
}
