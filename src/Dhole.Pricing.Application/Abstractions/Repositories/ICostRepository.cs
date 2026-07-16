using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface ICostRepository : IRepository<Cost, Guid>
{
    Task<bool> ExistsByNameAsync(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? portId,
        CostPortRole? portRole,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<Cost>> GetActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<CostDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<CostSelectDto>> GetForSelectAsync(
        string? search = null,
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        bool? isActive = true,
        CancellationToken cancellationToken = default
    );
}
