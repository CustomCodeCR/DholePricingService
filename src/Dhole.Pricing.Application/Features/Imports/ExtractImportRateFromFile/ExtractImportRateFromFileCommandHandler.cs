using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Imports.ExtractImportRateFromFile;

public sealed class ExtractImportRateFromFileCommandHandler(
    ExtractAndPersistFclPricingImportService extractionService,
    IImportFclRateRepository importRates,
    IPricingAuditService audit,
    IImportRateCacheService cache,
    IUnitOfWork unitOfWork
)
    : ICommandHandler<
        ExtractImportRateFromFileCommand,
        Result<ExtractAndPersistFclPricingImportResult>
    >
{
    public async Task<Result<ExtractAndPersistFclPricingImportResult>> HandleAsync(
        ExtractImportRateFromFileCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (command.FileContent.Length == 0)
        {
            return Result.Failure<ExtractAndPersistFclPricingImportResult>(
                PricingErrors.ImportFileIsEmpty
            );
        }

        if (string.IsNullOrWhiteSpace(command.ProfileSlug))
        {
            return Result.Failure<ExtractAndPersistFclPricingImportResult>(
                PricingErrors.ImportProfileRequired
            );
        }

        var importBatchId = Guid.NewGuid();

        ExtractAndPersistFclPricingImportResult extraction;

        try
        {
            extraction = await extractionService.ExecuteAsync(
                new ExtractAndPersistFclPricingImportRequest(
                    importBatchId,
                    ResolveSourceType(command.OriginalFileName, command.ContentType),
                    command.OriginalFileName,
                    command.ContentType,
                    command.ProfileSlug.Trim(),
                    command.FileContent,
                    command.RequestedBy,
                    command.RequestedByName,
                    command.CorrelationId
                ),
                cancellationToken
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<ExtractAndPersistFclPricingImportResult>(
                PricingErrors.InvalidImportFclRate
            );
        }

        if (!extraction.Success)
        {
            return Result.Success(extraction);
        }

        var createdRates = await importRates.GetByImportFclBatchIdAsync(
            importBatchId,
            cancellationToken
        );

        foreach (var importRate in createdRates)
        {
            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.ImportFclRateCreated,
                    Action: PricingAuditActions.Created,
                    EntityType: PricingAuditEntityTypes.ImportFclRate,
                    EntityId: importRate.Id,
                    ActorUserId: command.RequestedBy,
                    After: PricingAuditSnapshots.From(importRate),
                    Payload: new
                    {
                        importRate.Id,
                        importRate.ImportBatchId,
                        importRate.ExtractionRecordId,
                        importRate.SourceType,
                        importRate.ImportProfileId,
                        importRate.ImportProfileCode,
                        importRate.PolId,
                        importRate.Pol,
                        importRate.PoeId,
                        importRate.Poe,
                        importRate.PodId,
                        importRate.Pod,
                        importRate.CarrierId,
                        importRate.Carrier,
                        importRate.AgentId,
                        importRate.Agent,
                        importRate.ContainerTypeId,
                        importRate.ContainerType,
                        importRate.CurrencyId,
                        importRate.Currency,
                        importRate.OceanFreight,
                        importRate.TotalCost,
                        importRate.TotalSale,
                        importRate.Profit,
                        importRate.Margin,
                        SourceFileName = command.OriginalFileName,
                        extraction.ExtractionExecutionId,
                    }
                ),
                cancellationToken
            );
        }

        // Guarda los eventos de auditoría/outbox publicados
        // después del SaveChanges realizado por extractionService.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var importRate in createdRates)
        {
            await cache.RemoveImportRateCacheAsync(importRate.Id, importBatchId, cancellationToken);
        }

        await cache.RemoveImportRatesByBatchAsync(importBatchId, cancellationToken);

        await cache.RemoveImportRatesAsync(cancellationToken: cancellationToken);

        return Result.Success(extraction);
    }

    private static ImportSourceType ResolveSourceType(string originalFileName, string? contentType)
    {
        var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();

        var content = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (extension == "pdf" || content.Contains("pdf"))
        {
            return ImportSourceType.Pdf;
        }

        if (extension == "csv" || content.Contains("csv"))
        {
            return ImportSourceType.Csv;
        }

        if (
            extension is "eml" or "msg" or "html" or "htm" or "txt"
            || content.Contains("message/rfc822")
            || content.Contains("text/html")
            || content.Contains("text/plain")
        )
        {
            return ImportSourceType.Email;
        }

        return ImportSourceType.Excel;
    }
}
