using System.Data;
using System.Text.Json;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Worker.ExtractionImports;
using Dhole.Pricing.Worker.Streams;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

internal sealed class PricingImportFromExtractionWorker(
    ServiceDbContext dbContext,
    ExtractAndPersistFclPricingImportService importService,
    IIntegrationEventOutboxWriter outbox,
    IConfiguration configuration,
    ILogger<PricingImportFromExtractionWorker> logger
) : IBackgroundWorker
{
    private readonly string _leaseOwner =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public string Name => "pricing.import-from-extraction";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            !ReadBoolean(
                configuration["Pricing:ExtractionImportJobs:Enabled"],
                true
            )
        )
        {
            return;
        }

        await RecoverExpiredLeasesAsync(cancellationToken);
        var maxJobs = Math.Min(
            ReadPositiveInt(
                configuration[
                    "Pricing:ExtractionImportJobs:MaxJobsPerRun"
                ],
                10
            ),
            ReadPositiveInt(
                configuration[
                    "Pricing:ExtractionImportJobs:MaxConcurrentJobs"
                ],
                2
            )
        );
        for (var index = 0; index < maxJobs; index++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await ClaimNextJobAsync(cancellationToken);
            if (job is null)
            {
                break;
            }

            await ProcessAsync(job, cancellationToken);
        }
    }

    private async Task<PricingImportFromExtractionJob?> ClaimNextJobAsync(
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var leaseMinutes = ReadPositiveInt(
            configuration["Pricing:ExtractionImportJobs:LeaseMinutes"],
            5
        );
        return await dbContext.ExecuteInRetryableTransactionAsync<PricingImportFromExtractionJob?>(
            async () =>
            {
                var job = await dbContext.PricingImportFromExtractionJobs
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM pricing."PricingImportFromExtractionJobs"
                        WHERE status IN ('Pending', 'RetryScheduled')
                          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= {now})
                        ORDER BY created_at_utc
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """
                    )
                    .FirstOrDefaultAsync(cancellationToken);
                if (job is null)
                {
                    return null;
                }

                job.MarkProcessing(_leaseOwner, now.AddMinutes(leaseMinutes));
                await dbContext.SaveChangesAsync(cancellationToken);
                return job;
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
    }

    private async Task ProcessAsync(
        PricingImportFromExtractionJob job,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var integrationEvent =
                JsonSerializer.Deserialize<PricingImportFromExtractionRequestedIntegrationEvent>(
                    job.PayloadJson,
                    PricingExtractionStreamPayloadReader.JsonOptions
                ) ?? throw new PricingExtractionJobException(
                    "Pricing.InvalidExtractionPayload",
                    "No fue posible deserializar la solicitud de DataExtraction.",
                    isTransient: false
                );
            if (
                !Enum.TryParse<ImportSourceType>(
                    integrationEvent.SourceType,
                    ignoreCase: true,
                    out var sourceType
                )
                || !Enum.IsDefined(sourceType)
            )
            {
                throw new PricingExtractionJobException(
                    "Pricing.InvalidImportSourceType",
                    $"El origen '{integrationEvent.SourceType}' no es válido.",
                    isTransient: false
                );
            }

            var extraction = PricingEmailExtractionRecovery.Recover(
                DataExtractionPricingImportMapper.ToApplicationResult(
                    integrationEvent.Response,
                    integrationEvent.ExtractionExecutionId,
                    integrationEvent.PricingImportId
                ),
                sourceType,
                integrationEvent.Subject,
                integrationEvent.OriginalFileName
            );

            await dbContext.ExecuteInRetryableTransactionAsync(
                async () =>
                {
                    var persistenceResult = await importService.PersistExtractionAsync(
                        integrationEvent.PricingImportId,
                        sourceType,
                        extraction,
                        requestedBy: null,
                        cancellationToken
                    );
                    if (!persistenceResult.Success)
                    {
                        throw new PricingExtractionJobException(
                            persistenceResult.ErrorCode
                                ?? "Pricing.ExtractionImportFailed",
                            persistenceResult.ErrorMessage
                                ?? "No fue posible persistir la extracción.",
                            isTransient: false
                        );
                    }

                    var completedEvent =
                        new PricingImportFromExtractionCompletedIntegrationEvent(
                            Guid.NewGuid(),
                            job.ExternalRequestId,
                            job.EmailExtractionJobId,
                            job.ExtractionExecutionId,
                            job.PricingImportId,
                            persistenceResult.PersistedRows,
                            persistenceResult.SkippedRows,
                            job.CorrelationId,
                            DateTime.UtcNow
                        );
                    job.MarkCompleted(
                        persistenceResult.PersistedRows,
                        persistenceResult.SkippedRows
                    );
                    await outbox.WriteAsync(
                        typeof(PricingImportFromExtractionCompletedIntegrationEvent)
                            .FullName!,
                        ExtractionImportMessageTypes.Completed,
                        completedEvent,
                        job.CorrelationId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            logger.LogInformation(
                "Importación asíncrona completada. Pricing job {PricingJobId}; "
                    + "solicitud {RequestId}; lote {PricingImportId}; "
                    + "extracción {ExtractionExecutionId}; persistidas {PersistedRows}; "
                    + "omitidas {SkippedRows}; intentos {AttemptCount}; "
                    + "CorrelationId {CorrelationId}.",
                job.Id,
                job.ExternalRequestId,
                job.PricingImportId,
                job.ExtractionExecutionId,
                job.PersistedRows,
                job.SkippedRows,
                job.AttemptCount,
                job.CorrelationId
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PricingExtractionJobException exception)
        {
            await HandleFailureAsync(
                job.Id,
                exception.ErrorCode,
                exception.Message,
                exception.IsTransient,
                cancellationToken
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falló Pricing job {PricingJobId}.",
                job.Id
            );
            await HandleFailureAsync(
                job.Id,
                "Pricing.ExtractionImportUnexpectedError",
                exception.GetBaseException().Message,
                isTransient: true,
                cancellationToken
            );
        }
    }

    private async Task HandleFailureAsync(
        Guid jobId,
        string errorCode,
        string errorMessage,
        bool isTransient,
        CancellationToken cancellationToken
    )
    {
        dbContext.ChangeTracker.Clear();
        var job = await dbContext.PricingImportFromExtractionJobs
            .FirstOrDefaultAsync(
                item => item.Id == jobId,
                cancellationToken
            ) ?? throw new InvalidOperationException(
                "No se encontró el Pricing job que debe registrar el fallo."
            );
        if (
            job.Status
            is PricingImportFromExtractionJobStatus.Completed
                or PricingImportFromExtractionJobStatus.Failed
        )
        {
            return;
        }

        if (job.Status != PricingImportFromExtractionJobStatus.Processing)
        {
            throw new InvalidOperationException(
                $"El Pricing job {job.Id} no está en Processing al registrar el fallo."
            );
        }

        var shouldRetry =
            isTransient && job.AttemptCount < job.MaxAttemptCount;
        if (shouldRetry)
        {
            job.ScheduleRetry(
                errorCode,
                errorMessage,
                DateTime.UtcNow.AddSeconds(
                    ResolveRetryDelaySeconds(job.AttemptCount)
                )
            );
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Pricing job {PricingJobId} reprogramado. Solicitud {RequestId}; "
                    + "intento {AttemptCount}/{MaxAttemptCount}; código {ErrorCode}; "
                    + "próximo intento {NextAttemptAtUtc}; CorrelationId {CorrelationId}.",
                job.Id,
                job.ExternalRequestId,
                job.AttemptCount,
                job.MaxAttemptCount,
                errorCode,
                job.NextAttemptAtUtc,
                job.CorrelationId
            );
            return;
        }

        var failedEvent =
            new PricingImportFromExtractionFailedIntegrationEvent(
                Guid.NewGuid(),
                job.ExternalRequestId,
                job.EmailExtractionJobId,
                job.ExtractionExecutionId,
                job.PricingImportId,
                errorCode,
                Limit(errorMessage, 4000),
                isTransient,
                job.AttemptCount,
                job.CorrelationId,
                DateTime.UtcNow
            );
        await dbContext.ExecuteInRetryableTransactionAsync(
            async () =>
            {
                job.MarkFailed(errorCode, errorMessage);
                await outbox.WriteAsync(
                    typeof(PricingImportFromExtractionFailedIntegrationEvent).FullName!,
                    ExtractionImportMessageTypes.Failed,
                    failedEvent,
                    job.CorrelationId,
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        logger.LogError(
            "Pricing job {PricingJobId} falló definitivamente. "
                + "Solicitud {RequestId}; código {ErrorCode}; intentos {AttemptCount}; "
                + "CorrelationId {CorrelationId}.",
            job.Id,
            job.ExternalRequestId,
            errorCode,
            job.AttemptCount,
            job.CorrelationId
        );
    }

    private async Task RecoverExpiredLeasesAsync(
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var jobs = await dbContext.PricingImportFromExtractionJobs
            .Where(item =>
                item.Status
                    == PricingImportFromExtractionJobStatus.Processing
                && item.LeaseExpiresAtUtc.HasValue
                && item.LeaseExpiresAtUtc.Value < now
            )
            .OrderBy(item => item.LeaseExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            if (job.AttemptCount < job.MaxAttemptCount)
            {
                job.ScheduleRetry(
                    "Pricing.ExtractionImportLeaseExpired",
                    "El worker perdió el lease y la importación fue recuperada.",
                    now
                );
                continue;
            }

            const string errorCode = "Pricing.ExtractionImportLeaseExpired";
            const string errorMessage =
                "La importación agotó sus intentos después de perder el lease.";
            var failedEvent =
                new PricingImportFromExtractionFailedIntegrationEvent(
                    Guid.NewGuid(),
                    job.ExternalRequestId,
                    job.EmailExtractionJobId,
                    job.ExtractionExecutionId,
                    job.PricingImportId,
                    errorCode,
                    errorMessage,
                    true,
                    job.AttemptCount,
                    job.CorrelationId,
                    now
                );
            job.MarkFailed(errorCode, errorMessage);
            await outbox.WriteAsync(
                typeof(PricingImportFromExtractionFailedIntegrationEvent)
                    .FullName!,
                ExtractionImportMessageTypes.Failed,
                failedEvent,
                job.CorrelationId,
                cancellationToken
            );
        }

        if (jobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Se recuperaron {JobCount} Pricing jobs con lease vencido.",
                jobs.Count
            );
        }
    }

    private int ResolveRetryDelaySeconds(int attemptCount)
    {
        var delays = configuration
            .GetSection(
                "Pricing:ExtractionImportJobs:RetryDelaysSeconds"
            )
            .Get<int[]>();
        if (delays is not { Length: > 0 })
        {
            delays = [10, 60, 300];
        }

        return Math.Max(1, delays[Math.Min(attemptCount - 1, delays.Length - 1)]);
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
