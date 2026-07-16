using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class CostRepository(ServiceDbContext dbContext)
    : EfRepository<Cost, Guid>(dbContext),
        ICostRepository
{
    public Task<bool> ExistsByNameAsync(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? portId,
        CostPortRole? portRole,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var value = name.Trim().ToLowerInvariant();

        return dbContext.Costs.AnyAsync(
            x =>
                x.Name.ToLower() == value
                && x.CostType == costType
                && x.CostDetailType == costDetailType
                && x.PortId == portId
                && x.PortRole == portRole
                && x.CarrierId == carrierId
                && x.AgentId == agentId
                && !x.IsDeleted
                && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<Cost>> GetActiveCostsAsync(
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
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Where(x => !x.IsDeleted && x.IsActive),
            search: null,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId,
            isActive: true
        );

        return await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<CostDto>> GetPagedAsync(
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
    )
    {
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId,
            isActive
        );

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new CostDto(
                x.Id,
                x.Name,
                x.CostType.ToString(),
                x.CostDetailType.ToString(),
                x.CarrierId,
                x.CarrierName,
                x.CarrierCode,
                x.AgentId,
                x.AgentName,
                x.AgentCode,
                x.PortId,
                x.PortName,
                x.PortCode,
                x.PortRole.ToString(),
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.CostAmount,
                x.SaleAmount,
                x.UtilityAmount,
                x.Notes!,
                x.IsAccountant,
                x.IsActive
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<CostDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<CostSelectDto>> GetForSelectAsync(
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
    )
    {
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId,
            isActive
        );

        return await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .Take(100)
            .Select(x => new CostSelectDto(
                x.Id,
                x.Name,
                x.CostType.ToString(),
                x.CostDetailType.ToString(),
                x.CarrierId,
                x.CarrierName,
                x.CarrierCode,
                x.AgentId,
                x.AgentName,
                x.AgentCode,
                x.PortId,
                x.PortName,
                x.PortCode,
                x.PortRole.ToString(),
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.CostAmount,
                x.SaleAmount,
                x.UtilityAmount,
                x.Notes!,
                x.IsAccountant
            ))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Cost> ApplyFilters(
        IQueryable<Cost> query,
        string? search,
        CostType? costType,
        CostDetailType? costDetailType,
        Guid? carrierId,
        Guid? agentId,
        Guid? portId,
        CostPortRole? portRole,
        Guid? currencyId,
        bool? isActive
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();

            query = query.Where(x =>
                x.Name.ToLower().Contains(value)
                || x.CostType.ToString().ToLower().Contains(value)
                || x.CostDetailType.ToString().ToLower().Contains(value)
                || (x.CarrierName ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierCode ?? string.Empty).ToLower().Contains(value)
                || (x.AgentName ?? string.Empty).ToLower().Contains(value)
                || (x.AgentCode ?? string.Empty).ToLower().Contains(value)
                || x.PortName.ToLower().Contains(value)
                || x.PortCode.ToLower().Contains(value)
                || x.PortRole.ToString().ToLower().Contains(value)
                || x.CurrencyName.ToLower().Contains(value)
                || x.CurrencyCode.ToLower().Contains(value)
                || (x.Notes ?? string.Empty).ToLower().Contains(value)
            );
        }

        if (costType.HasValue)
        {
            query = query.Where(x => x.CostType == costType.Value);
        }

        if (costDetailType.HasValue)
        {
            query = query.Where(x => x.CostDetailType == costDetailType.Value);
        }

        if (carrierId.HasValue)
        {
            query = query.Where(x => x.CarrierId == carrierId.Value);
        }

        if (agentId.HasValue)
        {
            query = query.Where(x => x.AgentId == agentId.Value);
        }

        if (portId.HasValue)
        {
            query = query.Where(x => x.PortId == portId.Value);
        }

        if (portRole.HasValue)
        {
            query = query.Where(x => x.PortRole == portRole.Value);
        }

        if (currencyId.HasValue)
        {
            query = query.Where(x => x.CurrencyId == currencyId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        return query;
    }
}
