using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Abstractions.Cache;

public interface ICostCacheService
{
    Task<CostDto?> GetCostByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetCostByIdAsync(
        Guid id,
        CostDto cost,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveCostByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CostSelectDto>?> GetCostsSelectAsync(
        CancellationToken cancellationToken = default
    );

    Task SetCostsSelectAsync(
        IReadOnlyCollection<CostSelectDto> costs,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveCostsSelectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CostDto>?> GetActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    );

    Task SetActiveCostsAsync(
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
    );

    Task RemoveActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveCostCacheAsync(Guid costId, CancellationToken cancellationToken = default);
}
