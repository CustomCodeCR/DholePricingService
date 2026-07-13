using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Infrastructure.Cache;

public sealed class RateHeaderCacheService(ICacheService cache) : IRateHeaderCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan VersionExpiration = TimeSpan.FromDays(3650);

    public Task<RateDto?> GetRateHeaderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<RateDto>(RateHeaderCacheKeys.RateHeaderById(id), cancellationToken);
    }

    public Task SetRateHeaderByIdAsync(
        Guid id,
        RateDto rateHeader,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            RateHeaderCacheKeys.RateHeaderById(id),
            rateHeader,
            Options(expiration),
            cancellationToken
        );
    }

    public Task RemoveRateHeaderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(RateHeaderCacheKeys.RateHeaderById(id), cancellationToken);
    }

    public Task<IReadOnlyCollection<RateSelectDto>?> GetRateHeadersSelectAsync(
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<IReadOnlyCollection<RateSelectDto>>(
            RateHeaderCacheKeys.RateHeadersSelect(),
            cancellationToken
        );
    }

    public Task SetRateHeadersSelectAsync(
        IReadOnlyCollection<RateSelectDto> rateHeaders,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            RateHeaderCacheKeys.RateHeadersSelect(),
            rateHeaders,
            Options(expiration),
            cancellationToken
        );
    }

    public Task RemoveRateHeadersSelectAsync(CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(RateHeaderCacheKeys.RateHeadersSelect(), cancellationToken);
    }

    public async Task<IReadOnlyCollection<RateDto>?> GetValidRateHeadersAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = RateHeaderCacheKeys.ValidRateHeaders(
            version,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            quoteDate
        );

        return await cache.GetAsync<IReadOnlyCollection<RateDto>>(key, cancellationToken);
    }

    public async Task SetValidRateHeadersAsync(
        IReadOnlyCollection<RateDto> rateHeaders,
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = RateHeaderCacheKeys.ValidRateHeaders(
            version,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            quoteDate
        );

        await cache.SetAsync(key, rateHeaders, Options(expiration), cancellationToken);
    }

    public async Task RemoveValidRateHeadersAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = RateHeaderCacheKeys.ValidRateHeaders(
            version,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            quoteDate
        );

        await cache.RemoveAsync(key, cancellationToken);
    }

    public async Task RemoveRateHeaderCacheAsync(
        Guid rateHeaderId,
        CancellationToken cancellationToken = default
    )
    {
        await RemoveRateHeaderByIdAsync(rateHeaderId, cancellationToken);

        await RemoveRateHeadersSelectAsync(cancellationToken);
        await RotateQueryVersionAsync(cancellationToken);
    }

    private static CacheEntryOptions Options(TimeSpan? expiration)
    {
        return CacheEntryOptions.Default(expiration ?? DefaultExpiration);
    }

    private async Task<string> GetQueryVersionAsync(CancellationToken cancellationToken)
    {
        var version = await cache.GetAsync<string>(
            RateHeaderCacheKeys.QueryVersion,
            cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        version = NewVersion();

        await cache.SetAsync(
            RateHeaderCacheKeys.QueryVersion,
            version,
            CacheEntryOptions.Default(VersionExpiration),
            cancellationToken
        );

        return version;
    }

    private Task RotateQueryVersionAsync(CancellationToken cancellationToken)
    {
        return cache.SetAsync(
            RateHeaderCacheKeys.QueryVersion,
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
