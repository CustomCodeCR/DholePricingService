using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Features.Imports.ExtractImportRateFromFile;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>Persistent, bounded worker: no AI processing happens in the browser HTTP request.</summary>
public sealed class ManualImportExtractionWorker(
    IServiceScopeFactory scopes,
    ILogger<ManualImportExtractionWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(30);
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // API migrations are applied before jobs are consumed; a short delay prevents
        // startup races while another container is finishing its migration.
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Manual rate extraction worker failed its polling iteration.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        Guid? id;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            // Atomic claim across API replicas. SKIP LOCKED ensures only one worker owns a job.
            id = await db.Database.SqlQueryRaw<Guid>(
                """
                UPDATE pricing."ManualImportExtractionJobs"
                   SET status = 'Processing',
                       attempt_count = attempt_count + 1,
                       lease_expires_at_utc = NOW() + INTERVAL '30 minutes'
                 WHERE id = (
                     SELECT id FROM pricing."ManualImportExtractionJobs"
                     WHERE (status = 'Pending'
                         OR (status = 'Processing' AND lease_expires_at_utc < NOW()))
                       AND attempt_count < 3
                     ORDER BY created_at_utc
                     FOR UPDATE SKIP LOCKED LIMIT 1
                 )
                RETURNING id AS "Value"
                """
            ).FirstOrDefaultAsync(cancellationToken);
        }

        if (id is null || id == Guid.Empty) return;

        await using var processingScope = scopes.CreateAsyncScope();
        var dbContext = processingScope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var dispatcher = processingScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var job = await dbContext.ManualImportExtractionJobs
            .SingleAsync(x => x.Id == id, cancellationToken);

        try
        {
            // A worker can crash after rates have been committed but before job completion.
            // The stable batch identifier prevents a recovered attempt from inserting duplicates.
            var alreadyCreated = await dbContext.ImportFclRates
                .AsNoTracking()
                .CountAsync(x => x.ImportBatchId == job.Id, cancellationToken);

            ExtractAndPersistFclPricingImportResult extraction;
            if (alreadyCreated > 0)
            {
                extraction = new ExtractAndPersistFclPricingImportResult(
                    true, null, alreadyCreated, 0, [], null, null);
            }
            else
            {
                var result = await dispatcher.DispatchAsync(
                    new ExtractImportRateFromFileCommand(
                        job.OriginalFileName,
                        job.ContentType,
                        job.ProfileSlug,
                        job.FileContent,
                        job.RequestedBy,
                        job.RequestedByName,
                        job.CorrelationId,
                        job.Id
                    ), cancellationToken);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(result.Error.Message);

                extraction = result.Value;
            }

            job.Status = extraction.Success ? "Completed" : "Failed";
            job.ResultJson = JsonSerializer.Serialize(extraction);
            job.ErrorMessage = extraction.Success ? null
                : (extraction.ErrorMessage ?? "No se pudo completar la extracción.");
            job.CompletedAtUtc = DateTime.UtcNow;
            job.LeaseExpiresAtUtc = null;
            job.FileContent = [];
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Manual pricing import {JobId} ended with {Status}; persisted {Count} rates.",
                job.Id, job.Status, extraction.PersistedRows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave lease for recovery if the process is shutting down.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual pricing import {JobId} attempt {Attempt} failed.",
                job.Id, job.AttemptCount);
            // Repository/audit clients may have left tracked entities in the same scope.
            // Complete the job state in a fresh context.
            await using var failureScope = scopes.CreateAsyncScope();
            var failureDb = failureScope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            var failed = await failureDb.ManualImportExtractionJobs
                .SingleAsync(x => x.Id == job.Id, cancellationToken);
            failed.Status = failed.AttemptCount >= MaxAttempts ? "Failed" : "Pending";
            failed.ErrorMessage = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            failed.LeaseExpiresAtUtc = null;
            if (failed.Status == "Failed")
            {
                failed.CompletedAtUtc = DateTime.UtcNow;
                failed.FileContent = [];
            }
            await failureDb.SaveChangesAsync(cancellationToken);
        }
    }
}
