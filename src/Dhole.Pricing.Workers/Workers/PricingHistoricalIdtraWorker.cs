using System.Data;
using System.Text.RegularExpressions;
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
                var historical = rate.IdtraNumber!.Trim();
                var match = Regex.Match(historical, @"^IDTRA(?:999\.)?([0-9]+)$", RegexOptions.CultureInvariant);
                if (rate.Status != RateStatus.AcceptedByClient || rate.AcceptedAtUtc is null ||
                    rate.AcceptedAtUtc.Value.Year < 2000 || !match.Success || historical.Length > 32 ||
                    !long.TryParse(match.Groups[1].Value, out var number) || number is < 1 or > 999999999999L ||
                    !Guid.TryParse(Convert.ToString(rate.UpdatedBy), out var actor) || actor == Guid.Empty ||
                    string.IsNullOrWhiteSpace(rate.QuoNumber))
                {
                    skipped++;
                    logger.LogWarning("Historical IDTRA review required for Pricing rate {RateId}: ambiguous identifier or missing definitive acceptance/source identity.", rate.Id);
                    continue;
                }
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var key = "historical-idtra:" + rate.Id.ToString("N");
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0));", ct);
                if (await db.OutboxMessages.AnyAsync(x => x.EventName == "pricing.quote.accepted.v1" && x.CorrelationId == key, ct))
                {
                    alreadyQueued++;
                    await transaction.CommitAsync(ct);
                    continue;
                }
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
                    skipped++;
                    logger.LogWarning("Historical IDTRA review required for unselected or ambiguous alternative {RateId}", rate.Id);
                    await transaction.CommitAsync(ct);
                    continue;
                }
                var parent = alternatives.Length == 1 ? alternatives[0] : rate.Id;
                await outbox.WriteAsync("pricing.quote.accepted.v1", "pricing.quote.accepted.v1",
                    AcceptedQuoteSnapshot.Build(rate, parent, null, actor, historical), key, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                queued++;
            }
            if (rates.Count < batch) break;
        }
        logger.LogInformation("Historical IDTRA scan: queued={Queued}; already queued={Existing}; review={Review}", queued, alreadyQueued, skipped);
    }
}
