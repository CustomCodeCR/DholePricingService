using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Worker.ExtractionImports;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Streams;

internal sealed class PricingImportFromExtractionRequestedStreamHandler(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<PricingImportFromExtractionRequestedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    public string MessageType => ExtractionImportMessageTypes.Requested;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            PricingExtractionStreamPayloadReader.Read<PricingImportFromExtractionRequestedIntegrationEvent>(
                envelope
            );
        Validate(integrationEvent);

        var exists = await dbContext.PricingImportFromExtractionJobs.AnyAsync(
            item => item.ExternalRequestId == integrationEvent.RequestId,
            cancellationToken
        );
        if (exists)
        {
            return;
        }

        var job = PricingImportFromExtractionJob.Create(
            integrationEvent.RequestId,
            integrationEvent.EmailExtractionJobId,
            integrationEvent.ExtractionExecutionId,
            integrationEvent.PricingImportId,
            JsonSerializer.Serialize(
                integrationEvent,
                PricingExtractionStreamPayloadReader.JsonOptions
            ),
            integrationEvent.CorrelationId,
            ReadPositiveInt(
                configuration[
                    "Pricing:ExtractionImportJobs:MaxRetryCount"
                ],
                3
            )
        );
        await dbContext.PricingImportFromExtractionJobs.AddAsync(
            job,
            cancellationToken
        );
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (
                await dbContext.PricingImportFromExtractionJobs.AnyAsync(
                    item =>
                        item.ExternalRequestId
                        == integrationEvent.RequestId,
                    cancellationToken
                )
            )
            {
                logger.LogDebug(
                    "La solicitud Pricing {RequestId} ya fue persistida por otro consumidor.",
                    integrationEvent.RequestId
                );
                return;
            }

            throw;
        }

        logger.LogInformation(
            "Solicitud de importación persistida. Pricing job {PricingJobId}; "
                + "solicitud {RequestId}; trabajo de correo {EmailExtractionJobId}; "
                + "extracción {ExtractionExecutionId}; lote {PricingImportId}; "
                + "CorrelationId {CorrelationId}.",
            job.Id,
            job.ExternalRequestId,
            job.EmailExtractionJobId,
            job.ExtractionExecutionId,
            job.PricingImportId,
            job.CorrelationId
        );
    }

    private static void Validate(
        PricingImportFromExtractionRequestedIntegrationEvent integrationEvent
    )
    {
        if (
            integrationEvent.RequestId == Guid.Empty
            || integrationEvent.EmailExtractionJobId == Guid.Empty
            || integrationEvent.ExtractionExecutionId == Guid.Empty
            || integrationEvent.PricingImportId == Guid.Empty
            || string.IsNullOrWhiteSpace(integrationEvent.SourceType)
            || string.IsNullOrWhiteSpace(integrationEvent.CorrelationId)
            || integrationEvent.Response is null
        )
        {
            throw new InvalidOperationException(
                "La solicitud asíncrona de importación está incompleta."
            );
        }
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
