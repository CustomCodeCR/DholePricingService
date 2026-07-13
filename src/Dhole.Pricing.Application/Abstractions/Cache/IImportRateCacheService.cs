using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Abstractions.Cache;

public interface IImportRateCacheService
{
    Task<ImportRateDto?> GetImportRateByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    Task SetImportRateByIdAsync(
        Guid id,
        ImportRateDto importFclRate,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveImportRateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ImportRateDto>?> GetImportRatesByBatchAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    );

    Task SetImportRatesByBatchAsync(
        Guid importBatchId,
        IReadOnlyCollection<ImportRateDto> importFclRates,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveImportRatesByBatchAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ImportRateDto>?> GetImportRatesAsync(
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    );

    Task SetImportRatesAsync(
        IReadOnlyCollection<ImportRateDto> importFclRates,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveImportRatesAsync(
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveImportRateCacheAsync(
        Guid importFclRateId,
        Guid? importBatchId = null,
        CancellationToken cancellationToken = default
    );
}
