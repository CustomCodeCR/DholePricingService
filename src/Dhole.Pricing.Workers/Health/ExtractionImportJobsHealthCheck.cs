using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhole.Pricing.Worker.Health;

internal sealed class ExtractionImportJobsHealthCheck(
    ServiceDbContext dbContext,
    IConfiguration configuration
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var statuses = await dbContext.PricingImportFromExtractionJobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                item => item.Status,
                item => item.Count,
                cancellationToken
            );
        var now = DateTime.UtcNow;
        var staleJobs = await dbContext.PricingImportFromExtractionJobs
            .AsNoTracking()
            .CountAsync(
                job =>
                    job.Status
                        == PricingImportFromExtractionJobStatus.Processing
                    && job.LeaseExpiresAtUtc.HasValue
                    && job.LeaseExpiresAtUtc.Value < now,
                cancellationToken
            );
        var outboxPending = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Pending,
            cancellationToken
        );
        var outboxFailed = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Failed,
            cancellationToken
        );
        var inboxPending = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Pending,
            cancellationToken
        );
        var inboxFailed = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Failed,
            cancellationToken
        );
        var backlog =
            Get(statuses, PricingImportFromExtractionJobStatus.Pending)
            + Get(
                statuses,
                PricingImportFromExtractionJobStatus.RetryScheduled
            );
        var warningThreshold = ReadPositiveInt(
            configuration[
                "Monitoring:AsyncEmail:BacklogWarningThreshold"
            ],
            100
        );
        var data = new Dictionary<string, object>
        {
            ["pricing_import_jobs_pending"] = Get(
                statuses,
                PricingImportFromExtractionJobStatus.Pending
            ),
            ["pricing_import_jobs_retry_scheduled"] = Get(
                statuses,
                PricingImportFromExtractionJobStatus.RetryScheduled
            ),
            ["pricing_import_jobs_failed"] = Get(
                statuses,
                PricingImportFromExtractionJobStatus.Failed
            ),
            ["pricing_import_jobs_stale"] = staleJobs,
            ["outbox_pending"] = outboxPending,
            ["outbox_failed"] = outboxFailed,
            ["inbox_pending"] = inboxPending,
            ["inbox_failed"] = inboxFailed,
        };

        return staleJobs > 0
            || outboxFailed > 0
            || inboxFailed > 0
            || backlog > warningThreshold
            || outboxPending > warningThreshold
            || inboxPending > warningThreshold
            ? HealthCheckResult.Degraded(
                "Pricing tiene backlog de importaciones asíncronas.",
                data: data
            )
            : HealthCheckResult.Healthy(
                "Las importaciones asíncronas de Pricing están operativas.",
                data
            );
    }

    private static int Get(
        IReadOnlyDictionary<PricingImportFromExtractionJobStatus, int> values,
        PricingImportFromExtractionJobStatus status
    )
    {
        return values.TryGetValue(status, out var count) ? count : 0;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
