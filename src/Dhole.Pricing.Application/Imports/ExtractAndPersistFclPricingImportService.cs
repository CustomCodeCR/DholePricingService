using System.Security.Cryptography;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Imports;

public sealed class ExtractAndPersistFclPricingImportService(
    IDataExtractionFclPricingClient dataExtractionClient,
    IImportFclRateRepository importFclRateRepository,
    IUnitOfWork unitOfWork
)
{
    public async Task<ExtractAndPersistFclPricingImportResult> ExecuteAsync(
        ExtractAndPersistFclPricingImportRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.FileContent.Length == 0)
        {
            throw new InvalidOperationException("El archivo de importación está vacío.");
        }

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString()
            : request.CorrelationId.Trim();

        var extraction = await dataExtractionClient.ExtractAsync(
            new DataExtractionFclPricingRequest(
                request.ImportBatchId,
                correlationId,
                request.OriginalFileName,
                request.ContentType,
                Path.GetExtension(request.OriginalFileName),
                request.FileContent.LongLength,
                Convert.ToHexString(SHA256.HashData(request.FileContent)).ToLowerInvariant(),
                request.ProfileSlug,
                request.RequestedBy,
                request.RequestedByName,
                request.FileContent
            ),
            cancellationToken
        );

        if (!extraction.Success)
        {
            return new ExtractAndPersistFclPricingImportResult(
                false,
                extraction.ExtractionExecutionId,
                0,
                extraction.Summary.InvalidRows,
                extraction.Issues,
                extraction.ErrorCode,
                extraction.ErrorMessage
            );
        }

        return await PersistExtractionAsync(
            request.ImportBatchId,
            request.SourceType,
            extraction,
            request.RequestedBy,
            cancellationToken
        );
    }

    public async Task<ExtractAndPersistFclPricingImportResult> PersistExtractionAsync(
        Guid importBatchId,
        ImportSourceType sourceType,
        DataExtractionFclPricingResult extraction,
        Guid? requestedBy,
        CancellationToken cancellationToken = default
    )
    {
        if (importBatchId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "El identificador del lote de Pricing es requerido."
            );
        }

        if (!extraction.Success)
        {
            return new ExtractAndPersistFclPricingImportResult(
                false,
                extraction.ExtractionExecutionId,
                0,
                extraction.Summary.InvalidRows,
                extraction.Issues,
                extraction.ErrorCode,
                extraction.ErrorMessage
            );
        }

        var mapped = StandardizedImportFclRateFactory.CreateRates(
            importBatchId,
            sourceType,
            extraction,
            requestedBy
        );

        if (mapped.Rates.Count == 0)
        {
            return new ExtractAndPersistFclPricingImportResult(
                false,
                extraction.ExtractionExecutionId,
                0,
                mapped.SkippedExtractionRowIds.Count,
                extraction.Issues,
                "Pricing.NoUsableExtractionRows",
                BuildNoUsableRowsMessage(extraction)
            );
        }

        var existingExtractionRecordIds = (
            await importFclRateRepository.GetByImportFclBatchIdAsync(
                importBatchId,
                cancellationToken
            )
        )
            .Select(x => x.ExtractionRecordId)
            .ToHashSet();

        var duplicateExtractionRecordIds = mapped
            .Rates.Where(x => existingExtractionRecordIds.Contains(x.ExtractionRecordId))
            .Select(x => x.ExtractionRecordId)
            .ToHashSet();

        var newRates = mapped
            .Rates.Where(x => !existingExtractionRecordIds.Contains(x.ExtractionRecordId))
            .ToArray();

        foreach (var rate in newRates)
        {
            await importFclRateRepository.AddAsync(rate, cancellationToken);
        }

        if (newRates.Length > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ExtractAndPersistFclPricingImportResult(
            true,
            extraction.ExtractionExecutionId,
            newRates.Length,
            mapped.SkippedExtractionRowIds.Count + duplicateExtractionRecordIds.Count,
            extraction.Issues,
            null,
            null
        );
    }
    private static string BuildNoUsableRowsMessage(
        DataExtractionFclPricingResult extraction
    )
    {
        const string baseMessage =
            "Ninguna fila pudo guardarse porque faltan datos estructurales requeridos, fechas válidas o un monto de tarifa. Los valores no encontrados en Config ya no bloquean la importación.";

        if (extraction.Rows.Count == 0)
        {
            return $"{baseMessage} DataExtraction no devolvió filas.";
        }

        var blockingCodes = extraction.Issues
            .Where(issue => issue.IsBlocking)
            .Select(issue => issue.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var missingPoe = extraction.Rows.Count(row =>
            string.IsNullOrWhiteSpace(row.PortOfExit)
            && string.IsNullOrWhiteSpace(row.DestinationPort)
        );
        var missingDates = extraction.Rows.Count(row =>
            !row.ValidFrom.HasValue
            || !row.ValidTo.HasValue
            || row.ValidTo < row.ValidFrom
        );
        var missingAmount = extraction.Rows.Count(row =>
            !row.OceanFreight.HasValue && !row.TotalSale.HasValue
        );
        var missingStructure = extraction.Rows.Count(row =>
            string.IsNullOrWhiteSpace(row.OriginPort)
            || string.IsNullOrWhiteSpace(row.ContainerType)
            || string.IsNullOrWhiteSpace(row.Carrier)
        );

        var details = new List<string>
        {
            $"filas recibidas: {extraction.Rows.Count}",
            $"sin estructura: {missingStructure}",
            $"sin POE recuperable: {missingPoe}",
            $"sin vigencia válida: {missingDates}",
            $"sin monto: {missingAmount}",
        };
        if (blockingCodes.Length > 0)
        {
            details.Add($"bloqueos: {string.Join(", ", blockingCodes)}");
        }

        return $"{baseMessage} Detalle: {string.Join("; ", details)}.";
    }
}

public sealed record ExtractAndPersistFclPricingImportRequest(
    Guid ImportBatchId,
    ImportSourceType SourceType,
    string OriginalFileName,
    string? ContentType,
    string? ProfileSlug,
    byte[] FileContent,
    Guid? RequestedBy,
    string? RequestedByName,
    string? CorrelationId = null
);

public sealed record ExtractAndPersistFclPricingImportResult(
    bool Success,
    Guid? ExtractionExecutionId,
    int PersistedRows,
    int SkippedRows,
    IReadOnlyCollection<DataExtractionFclPricingIssue> Issues,
    string? ErrorCode,
    string? ErrorMessage
);
