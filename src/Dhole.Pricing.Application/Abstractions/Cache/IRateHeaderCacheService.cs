using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Abstractions.Cache;

public interface IRateHeaderCacheService
{
    Task<RateDto?> GetRateHeaderByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetRateHeaderByIdAsync(
        Guid id,
        RateDto rateHeader,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveRateHeaderByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RateSelectDto>?> GetRateHeadersSelectAsync(
        CancellationToken cancellationToken = default
    );

    Task SetRateHeadersSelectAsync(
        IReadOnlyCollection<RateSelectDto> rateHeaders,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveRateHeadersSelectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RateDto>?> GetValidRateHeadersAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    );

    Task SetValidRateHeadersAsync(
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
    );

    Task RemoveValidRateHeadersAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveRateHeaderCacheAsync(
        Guid rateHeaderId,
        CancellationToken cancellationToken = default
    );
}
