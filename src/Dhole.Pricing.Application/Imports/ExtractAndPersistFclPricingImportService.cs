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
            throw new InvalidOperationException("El archivo de importación está vacío.");

        if (string.IsNullOrWhiteSpace(request.ProfileSlug))
            throw new InvalidOperationException(
                "Debe seleccionar un perfil de pricing-imports-profiles."
            );

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

        var mapped = StandardizedImportFclRateFactory.CreateRates(
            request.ImportBatchId,
            request.SourceType,
            extraction,
            request.RequestedBy
        );

        foreach (var rate in mapped.Rates)
        {
            await importFclRateRepository.AddAsync(rate, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExtractAndPersistFclPricingImportResult(
            true,
            extraction.ExtractionExecutionId,
            mapped.Rates.Count,
            mapped.SkippedExtractionRowIds.Count,
            extraction.Issues,
            null,
            null
        );
    }
}

public sealed record ExtractAndPersistFclPricingImportRequest(
    Guid ImportBatchId,
    ImportSourceType SourceType,
    string OriginalFileName,
    string? ContentType,
    string ProfileSlug,
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
