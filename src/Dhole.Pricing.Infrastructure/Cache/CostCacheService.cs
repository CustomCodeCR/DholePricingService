using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Infrastructure.Cache;

public sealed class CostCacheService(ICacheService cache) : ICostCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VersionExpiration = TimeSpan.FromDays(3650);

    public Task<CostDto?> GetCostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<CostDto>(CostCacheKeys.CostById(id), cancellationToken);
    }

    public Task SetCostByIdAsync(
        Guid id,
        CostDto cost,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            CostCacheKeys.CostById(id),
            cost,
            Options(expiration),
            cancellationToken
        );
    }

    public Task RemoveCostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(CostCacheKeys.CostById(id), cancellationToken);
    }

    public Task<IReadOnlyCollection<CostSelectDto>?> GetCostsSelectAsync(
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<IReadOnlyCollection<CostSelectDto>>(
            CostCacheKeys.CostsSelect(),
            cancellationToken
        );
    }

    public Task SetCostsSelectAsync(
        IReadOnlyCollection<CostSelectDto> costs,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            CostCacheKeys.CostsSelect(),
            costs,
            Options(expiration),
            cancellationToken
        );
    }

    public Task RemoveCostsSelectAsync(CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(CostCacheKeys.CostsSelect(), cancellationToken);
    }

    public async Task<IReadOnlyCollection<CostDto>?> GetActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = CostCacheKeys.ActiveCosts(
            version,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId
        );

        return await cache.GetAsync<IReadOnlyCollection<CostDto>>(key, cancellationToken);
    }

    public async Task SetActiveCostsAsync(
        IReadOnlyCollection<CostDto> costs,
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = CostCacheKeys.ActiveCosts(
            version,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId
        );

        await cache.SetAsync(key, costs, Options(expiration), cancellationToken);
    }

    public async Task RemoveActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    )
    {
        var version = await GetQueryVersionAsync(cancellationToken);

        var key = CostCacheKeys.ActiveCosts(
            version,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId
        );

        await cache.RemoveAsync(key, cancellationToken);
    }

    public async Task RemoveCostCacheAsync(
        Guid costId,
        CancellationToken cancellationToken = default
    )
    {
        await RemoveCostByIdAsync(costId, cancellationToken);
        await RemoveCostsSelectAsync(cancellationToken);
        await RotateQueryVersionAsync(cancellationToken);
    }

    private static CacheEntryOptions Options(TimeSpan? expiration)
    {
        return CacheEntryOptions.Default(expiration ?? DefaultExpiration);
    }

    private async Task<string> GetQueryVersionAsync(CancellationToken cancellationToken)
    {
        var version = await cache.GetAsync<string>(CostCacheKeys.QueryVersion, cancellationToken);

        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        version = NewVersion();

        await cache.SetAsync(
            CostCacheKeys.QueryVersion,
            version,
            CacheEntryOptions.Default(VersionExpiration),
            cancellationToken
        );

        return version;
    }

    private Task RotateQueryVersionAsync(CancellationToken cancellationToken)
    {
        return cache.SetAsync(
            CostCacheKeys.QueryVersion,
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
