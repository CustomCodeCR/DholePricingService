using System.Data;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

/// <summary>Staging-controlled historical import; no mutation of Pricing records.</summary>
internal sealed class PricingHistoricalIdtraWorker(
    ServiceDbContext db,
    IIntegrationEventOutboxWriter outbox,
    IConfiguration configuration,
    ILogger<PricingHistoricalIdtraWorker> logger) : IBackgroundWorker
{
    public string Name => "pricing.historical-idtra";
    public async Task ExecuteAsync(IWorkerExecutionContext context, CancellationToken ct)
    {
        if (!configuration.GetValue("Pricing:HistoricalIdtra:Enabled", false)) return;
        // Do not fabricate a commercial acceptance date or associate an ambiguous
        // legacy identifier. Surface excluded records in the migration log.
        const int batch = 100;
        var queued = 0; var skipped = 0; var alreadyQueued = 0;
        for (var offset = 0; ; offset += batch)
        {
            var rates = await db.RateHeaders.AsNoTracking()
                .Where(r => !r.IsDeleted && r.IdtraNumber != null && r.IdtraNumber != "")
                .OrderBy(r => r.Id).Skip(offset).Take(batch)
                .Include(r => r.RateDetails).Include(r => r.RateContainers).Include(r => r.RateServices)
                .AsSplitQuery().ToListAsync(ct);
            if (rates.Count == 0) break;
            foreach (var rate in rates)
            {
                ct.ThrowIfCancellationRequested();
                if (rate.Status != RateStatus.AcceptedByClient || rate.AcceptedAtUtc is null ||
                    rate.AcceptedAtUtc.Value.Year < 2000 ||
                    !AcceptedQuoteSnapshot.TryReadHistoricalIdtras(rate.IdtraNumber, out var idtras) ||
                    !Guid.TryParse(Convert.ToString(rate.UpdatedBy), out var actor) || actor == Guid.Empty ||
                    string.IsNullOrWhiteSpace(rate.QuoNumber))
                {
                    skipped++;
                    logger.LogWarning("Historical IDTRA review required for Pricing rate {RateId}: ambiguous identifier or missing definitive acceptance/source identity.", rate.Id);
                    continue;
                }
                try
                {
                    var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        db.ChangeTracker.Clear();
                        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                        var key = "historical-idtra:" + rate.Id.ToString("N");
                        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0));", ct);
                        var alternatives = await db.Database.SqlQueryRaw<Guid>(
                            """
                            SELECT parent_rate_id AS "Value" FROM pricing.rate_quote_alternatives
                            WHERE rate_id = {0} AND status = 'Selected'
                            """, rate.Id).ToArrayAsync(ct);
                        var groupMembership = await db.Database.SqlQueryRaw<Guid>(
                            """
                            SELECT parent_rate_id AS "Value" FROM pricing.rate_quote_alternatives WHERE rate_id = {0}
                            """, rate.Id).ToArrayAsync(ct);
                        if (groupMembership.Length > 0 && alternatives.Length != 1)
                        {
                            await transaction.CommitAsync(ct);
                            return -1;
                        }
                        var parent = alternatives.Length == 1 ? alternatives[0] : rate.Id;
                        var inserted = 0;
                        foreach (var idtra in idtras)
                        {
                            var eventKey = idtras.Length == 1 ? key : key + ":" + idtra;
                            if (await db.OutboxMessages.AnyAsync(x => x.EventName == "pricing.quote.accepted.v1" &&
                                x.CorrelationId == eventKey, ct)) continue;
                            await outbox.WriteAsync("pricing.quote.accepted.v1", "pricing.quote.accepted.v1",
                                AcceptedQuoteSnapshot.Build(rate, parent, null, actor, idtra, idtras), eventKey, ct);
                            inserted++;
                        }
                        await db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        return inserted;
                    });
                    if (result > 0) queued += result;
                    else if (result == 0) alreadyQueued++;
                    else
                    {
                        skipped++;
                        logger.LogWarning("Historical IDTRA review required for unselected or ambiguous alternative {RateId}", rate.Id);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    skipped++;
                    logger.LogError(error, "Historical IDTRA enqueue failed for rate {RateId}; next scan will retry", rate.Id);
                }
                finally { db.ChangeTracker.Clear(); }
            }
            if (rates.Count < batch) break;
        }
        logger.LogInformation("Historical IDTRA scan: queued={Queued}; already queued={Existing}; review={Review}", queued, alreadyQueued, skipped);
    }
}
