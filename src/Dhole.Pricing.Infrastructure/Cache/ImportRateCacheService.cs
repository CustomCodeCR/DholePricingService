using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Infrastructure.Cache;

public sealed class ImportRateCacheService(ICacheService cache) : IImportRateCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan VersionExpiration = TimeSpan.FromDays(3650);

    public Task<ImportRateDto?> GetImportRateByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<ImportRateDto>(
            ImportRateCacheKeys.ImportRateById(id),
            cancellationToken
        );
    }

    public Task SetImportRateByIdAsync(
        Guid id,
        ImportRateDto importFclRate,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            ImportRateCacheKeys.ImportRateById(id),
            importFclRate,
            Options(expiration),
            cancellationToken
        );
    }

    public Task RemoveImportRateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(ImportRateCacheKeys.ImportRateById(id), cancellationToken);
    }

    public async Task<IReadOnlyCollection<ImportRateDto>?> GetImportRatesByBatchAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRatesByBatch(version, importBatchId);

        return await cache.GetAsync<IReadOnlyCollection<ImportRateDto>>(key, cancellationToken);
    }

    public async Task SetImportRatesByBatchAsync(
        Guid importBatchId,
        IReadOnlyCollection<ImportRateDto> importFclRates,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRatesByBatch(version, importBatchId);

        await cache.SetAsync(key, importFclRates, Options(expiration), cancellationToken);
    }

    public async Task RemoveImportRatesByBatchAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRatesByBatch(version, importBatchId);

        await cache.RemoveAsync(key, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ImportRateDto>?> GetImportRatesAsync(
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
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRates(
            version,
            importBatchId,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate
        );

        return await cache.GetAsync<IReadOnlyCollection<ImportRateDto>>(key, cancellationToken);
    }

    public async Task SetImportRatesAsync(
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
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRates(
            version,
            importBatchId,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate
        );

        await cache.SetAsync(key, importFclRates, Options(expiration), cancellationToken);
    }

    public async Task RemoveImportRatesAsync(
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
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = ImportRateCacheKeys.ImportRates(
            version,
            importBatchId,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate
        );

        await cache.RemoveAsync(key, cancellationToken);
    }

    public async Task RemoveImportRateCacheAsync(
        Guid importFclRateId,
        Guid? importBatchId = null,
        CancellationToken cancellationToken = default
    )
    {
        await RemoveImportRateByIdAsync(importFclRateId, cancellationToken);

        if (importBatchId.HasValue)
        {
            await RemoveImportRatesByBatchAsync(importBatchId.Value, cancellationToken);
        }

        await RotateQueryVersionAsync(cancellationToken);
    }

    private static CacheEntryOptions Options(TimeSpan? expiration)
    {
        return CacheEntryOptions.Default(expiration ?? DefaultExpiration);
    }

    private async Task<string> GetQueryVersionAsync(CancellationToken cancellationToken)
    {
        var version = await cache.GetAsync<string>(
            ImportRateCacheKeys.QueryVersion,
            cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        version = NewVersion();

        await cache.SetAsync(
            ImportRateCacheKeys.QueryVersion,
            version,
            CacheEntryOptions.Default(VersionExpiration),
            cancellationToken
        );

        return version;
    }

    private Task RotateQueryVersionAsync(CancellationToken cancellationToken)
    {
        return cache.SetAsync(
            ImportRateCacheKeys.QueryVersion,
            NewVersion(),
            CacheEntryOptions.Default(VersionExpiration),
            cancellationToken
        );
    }

    private static string NewVersion()
    {
        return Guid.NewGuid().ToString("N");
    }
}
