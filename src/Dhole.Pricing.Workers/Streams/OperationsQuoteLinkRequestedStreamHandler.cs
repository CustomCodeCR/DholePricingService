using System.Data;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Streams;

/// <summary>Only an explicitly requested, already accepted QUO can be linked. Never accepts a quote or changes its commercial terms.</summary>
internal sealed class OperationsQuoteLinkRequestedStreamHandler(
    ServiceDbContext db, IIntegrationEventOutboxWriter outbox, IPricingAuditService audit,
    IRateHeaderCacheService cache, ILogger<OperationsQuoteLinkRequestedStreamHandler> logger) : IRedisStreamMessageHandler
{
    public string MessageType => "operations.quote-link.requested.v1";
    private sealed record Request(Guid RequestId, Guid CaseId, Guid AccountId, Guid CompanyId,
        Guid RateId, Guid RequestedBy, string Idtra, string ModalityCode, string[] ExistingIdtras);

    public async Task HandleAsync(RedisStreamEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var request = PricingExtractionStreamPayloadReader.Read<Request>(envelope);
        if (new[] { request.RequestId, request.CaseId, request.AccountId, request.CompanyId, request.RateId, request.RequestedBy }.Any(x => x == Guid.Empty) ||
            !AcceptedQuoteSnapshot.TryReadHistoricalIdtras(request.Idtra, out var target) || target.Length != 1 ||
            request.ExistingIdtras is null || request.ExistingIdtras.Length > 30)
            throw new InvalidOperationException("Invalid Operations link request.");
        var correlation = "operations-quote-link:" + request.RequestId.ToString("N");
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({correlation}, 0));", cancellationToken);
            if (await db.OutboxMessages.AnyAsync(x => x.CorrelationId == correlation &&
                (x.EventName == "pricing.quote.linked.v1" || x.EventName == "pricing.quote-link.rejected.v1"), cancellationToken))
            {
                await tx.CommitAsync(cancellationToken);
                return;
            }
            await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM pricing.\"RateHeaders\" WHERE id = {0} FOR UPDATE", request.RateId).ToArrayAsync(cancellationToken);
            var rate = await db.RateHeaders.Include(x => x.RateDetails).Include(x => x.RateContainers)
                .Include(x => x.RateServices).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == request.RateId && !x.IsDeleted, cancellationToken);
            var memberships = await db.Database.SqlQueryRaw<Guid>("SELECT parent_rate_id AS \"Value\" FROM pricing.rate_quote_alternatives WHERE rate_id = {0}", request.RateId).ToArrayAsync(cancellationToken);
            var selected = await db.Database.SqlQueryRaw<Guid>("SELECT parent_rate_id AS \"Value\" FROM pricing.rate_quote_alternatives WHERE rate_id = {0} AND status = 'Selected'", request.RateId).ToArrayAsync(cancellationToken);
            var reason = rate is null || rate.Status != RateStatus.AcceptedByClient || !rate.AcceptedAtUtc.HasValue ||
                string.IsNullOrWhiteSpace(rate.QuoNumber) ? "QUOTE_NOT_ACCEPTED" :
                rate.ClientAccountId != request.AccountId ? "QUOTE_CUSTOMER_MISMATCH" :
                !string.Equals(rate.ShipmentMode.ToString(), request.ModalityCode, StringComparison.OrdinalIgnoreCase) ? "QUOTE_MODALITY_MISMATCH" :
                memberships.Length > 0 && selected.Length != 1 ? "QUOTE_OPTION_NOT_SELECTED" : null;
            string[] ids = [];
            if (reason is null)
            {
                var csv = string.Join(", ", new[] { rate!.IdtraNumber, request.Idtra }.Concat(request.ExistingIdtras).Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!AcceptedQuoteSnapshot.TryReadHistoricalIdtras(csv, out ids) || ids.Length > 30)
                    reason = "QUOTE_IDTRA_LIST_CONFLICT";
            }
            if (reason is not null)
            {
                await outbox.WriteAsync("pricing.quote-link.rejected.v1", "pricing.quote-link.rejected.v1",
                    new { caseId = request.CaseId, quoteLinkRequestId = request.RequestId, rateId = request.RateId, reason }, correlation, cancellationToken);
            }
            else
            {
                var before = PricingAuditSnapshots.From(rate!);
                rate!.SetIdtraNumber(string.Join(", ", ids), request.RequestedBy);
                await audit.PublishAsync(new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderUpdated, Action: PricingAuditActions.Updated,
                    EntityType: PricingAuditEntityTypes.RateHeader, EntityId: rate.Id,
                    ActorUserId: request.RequestedBy, Before: before, After: PricingAuditSnapshots.From(rate),
                    Payload: new { rate.Id, rate.IdtraNumber, request.CaseId, request.RequestId, Source = "ManualOperationsQuoteLink" }), cancellationToken);
                var parent = selected.Length == 1 ? selected[0] : rate.Id;
                await outbox.WriteAsync("pricing.quote.linked.v1", "pricing.quote.linked.v1",
                    AcceptedQuoteSnapshot.Build(rate, parent, null, request.RequestedBy, request.Idtra, ids, request.CaseId, request.RequestId),
                    correlation, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
        try { await cache.RemoveRateHeaderCacheAsync(request.RateId, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Linked QUO cache refresh will retry through realtime."); }
    }
}
