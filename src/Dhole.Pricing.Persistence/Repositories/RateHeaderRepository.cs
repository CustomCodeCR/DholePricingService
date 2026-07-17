using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class RateHeaderRepository(ServiceDbContext dbContext)
    : EfRepository<RateHeader, Guid>(dbContext),
        IRateHeaderRepository
{
    public Task<RateHeader?> GetByIdWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext
            .RateHeaders.Include(x => x.RateDetails)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RateHeader>> GetValidRateHeadersAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        RateStatus? status = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext
                .RateHeaders.AsNoTracking()
                .Include(x => x.RateDetails)
                .Where(x => !x.IsDeleted),
            search: null,
            idtraNumber: null,
            quoNumber: null,
            sourceImportFclRateId: null,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            status,
            requiredApproval: null,
            quoteDate,
            validFrom: null,
            validTo: null
        );

        if (!status.HasValue)
        {
            query = query.Where(x => x.Status == RateStatus.Approved);
        }

        return await query
            .OrderBy(x => x.CarrierName)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.PoeName)
            .ThenBy(x => x.PodName)
            .ThenBy(x => x.ContainerTypeName)
            .ThenBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RateHeader>> GetPendingApprovalAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext
                .RateHeaders.AsNoTracking()
                .Include(x => x.RateDetails)
                .Where(x => !x.IsDeleted),
            search: null,
            idtraNumber: null,
            quoNumber: null,
            sourceImportFclRateId: null,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            status: RateStatus.PendingApproval,
            requiredApproval: true,
            quoteDate: null,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderBy(x => x.AgentName)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.PoeName)
            .ThenBy(x => x.PodName)
            .ThenBy(x => x.ContainerTypeName)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<RateDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        string? idtraNumber = null,
        string? quoNumber = null,
        Guid? sourceImportFclRateId = null,
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        RateStatus? status = null,
        bool? requiredApproval = null,
        DateTime? quoteDate = null,
        DateTime? validFrom = null,
        DateTime? validTo = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.RateHeaders.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            idtraNumber,
            quoNumber,
            sourceImportFclRateId,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            status,
            requiredApproval,
            quoteDate,
            validFrom,
            validTo
        );

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.PoeName)
            .ThenBy(x => x.PodName)
            .ThenBy(x => x.ContainerTypeName)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new RateDto(
                x.Id,
                x.RateCode,
                x.RateName,
                x.SourceImportFclRateId,
                x.AgentId,
                x.AgentName,
                x.AgentCode,
                x.CarrierId,
                x.CarrierName,
                x.CarrierCode,
                x.PolId,
                x.PolName,
                x.PolCode,
                x.PoeId,
                x.PoeName,
                x.PoeCode,
                x.PodId,
                x.PodName,
                x.PodCode,
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.ContainerQuantity,
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.ClientName,
                x.IdtraNumber,
                x.QuoNumber,
                x.Includes,
                x.SubjectTo,
                x.Excludes,
                x.TransitDays,
                x.TotalCostAmount,
                x.TotalSaleAmount,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.RequiredApproval,
                x.Status.ToString(),
                x.RateDetails.OrderBy(d => d.CostDetailType)
                    .ThenBy(d => d.Name)
                    .Select(d => new RateDetailDto(
                        d.Id,
                        d.RateHeaderId,
                        d.CostId,
                        d.Name,
                        d.CostDetailType.ToString(),
                        d.CostType.ToString(),
                        d.CurrencyId,
                        d.CurrencyName,
                        d.CurrencyCode,
                        d.CostAmount,
                        d.SaleAmount,
                        d.UtilityAmount,
                        d.Quantity,
                        d.Notes
                    ))
                    .ToList()
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<RateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<RateSelectDto>> GetForSelectAsync(
        string? search = null,
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        RateStatus? status = null,
        bool? requiredApproval = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.RateHeaders.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            idtraNumber: null,
            quoNumber: null,
            sourceImportFclRateId: null,
            agentId,
            carrierId,
            polId,
            poeId,
            podId,
            containerTypeId,
            currencyId,
            status,
            requiredApproval,
            quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderByDescending(x => x.ValidFrom)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.PoeName)
            .ThenBy(x => x.PodName)
            .ThenBy(x => x.ContainerTypeName)
            .Take(100)
            .Select(x => new RateSelectDto(
                x.Id,
                BuildRateHeaderLabel(
                    x.AgentName!,
                    x.CarrierName!,
                    x.CarrierCode!,
                    x.PolCode,
                    x.PoeCode,
                    x.PodCode,
                    x.ContainerTypeCode,
                    x.CurrencyCode,
                    x.TotalSaleAmount,
                    x.MarginPercentage,
                    x.Status
                ),
                x.Status.ToString(),
                x.RequiredApproval
            ))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RateHeader> ApplyFilters(
        IQueryable<RateHeader> query,
        string? search,
        string? idtraNumber,
        string? quoNumber,
        Guid? sourceImportFclRateId,
        Guid? agentId,
        Guid? carrierId,
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? containerTypeId,
        Guid? currencyId,
        RateStatus? status,
        bool? requiredApproval,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = NormalizeSearchValue(search);

            query = query.Where(x =>
                x.RateCode.ToLower().Contains(value)
                || x.RateName.ToLower().Contains(value)
                || (x.ClientName ?? string.Empty).ToLower().Contains(value)
                || (x.IdtraNumber ?? string.Empty).ToLower().Contains(value)
                || (x.QuoNumber ?? string.Empty).ToLower().Contains(value)
                || (x.AgentName ?? string.Empty).ToLower().Contains(value)
                || (x.AgentCode ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierName ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierCode ?? string.Empty).ToLower().Contains(value)
                || x.PolName.ToLower().Contains(value)
                || x.PolCode.ToLower().Contains(value)
                || x.PoeName.ToLower().Contains(value)
                || x.PoeCode.ToLower().Contains(value)
                || x.PodName.ToLower().Contains(value)
                || x.PodCode.ToLower().Contains(value)
                || x.ContainerTypeName.ToLower().Contains(value)
                || x.ContainerTypeCode.ToLower().Contains(value)
                || x.CurrencyName.ToLower().Contains(value)
                || x.CurrencyCode.ToLower().Contains(value)
                || x.Status.ToString().ToLower().Contains(value)
            );
        }

        if (!string.IsNullOrWhiteSpace(idtraNumber))
        {
            var value = NormalizeSearchValue(idtraNumber);
            query = query.Where(x => (x.IdtraNumber ?? string.Empty).ToLower().Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(quoNumber))
        {
            var value = NormalizeSearchValue(quoNumber);
            query = query.Where(x => (x.QuoNumber ?? string.Empty).ToLower().Contains(value));
        }

        if (sourceImportFclRateId.HasValue)
        {
            query = query.Where(x => x.SourceImportFclRateId == sourceImportFclRateId.Value);
        }

        if (agentId.HasValue)
        {
            query = query.Where(x => x.AgentId == agentId.Value);
        }

        if (carrierId.HasValue)
        {
            query = query.Where(x => x.CarrierId == carrierId.Value);
        }

        if (polId.HasValue)
        {
            query = query.Where(x => x.PolId == polId.Value);
        }

        if (poeId.HasValue)
        {
            query = query.Where(x => x.PoeId == poeId.Value);
        }

        if (podId.HasValue)
        {
            query = query.Where(x => x.PodId == podId.Value);
        }

        if (containerTypeId.HasValue)
        {
            query = query.Where(x => x.ContainerTypeId == containerTypeId.Value);
        }

        if (currencyId.HasValue)
        {
            query = query.Where(x => x.CurrencyId == currencyId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (requiredApproval.HasValue)
        {
            query = query.Where(x => x.RequiredApproval == requiredApproval.Value);
        }

        if (quoteDate.HasValue)
        {
            var value = quoteDate.Value.Date;

            query = query.Where(x => x.ValidFrom.Date <= value && x.ValidTo.Date >= value);
        }

        if (validFrom.HasValue)
        {
            query = query.Where(x => x.ValidFrom.Date >= validFrom.Value.Date);
        }

        if (validTo.HasValue)
        {
            query = query.Where(x => x.ValidTo.Date <= validTo.Value.Date);
        }

        return query;
    }

    private static string BuildRateHeaderLabel(
        string agentName,
        string carrierName,
        string carrierCode,
        string polCode,
        string poeCode,
        string podCode,
        string containerTypeCode,
        string currencyCode,
        decimal totalSaleAmount,
        decimal marginPercentage,
        RateStatus status
    )
    {
        var agent = string.IsNullOrWhiteSpace(agentName) ? "Sin agente" : agentName.Trim();

        var carrier = !string.IsNullOrWhiteSpace(carrierCode)
            ? carrierCode.Trim()
            : carrierName.Trim();

        var route = string.IsNullOrWhiteSpace(poeCode)
            ? $"{polCode} → {podCode}"
            : $"{polCode} → {poeCode} → {podCode}";

        return $"{agent} | {carrier} | {route} | {containerTypeCode} | "
            + $"{currencyCode} {totalSaleAmount:N2} | Margen {marginPercentage:N2}% | {status}";
    }

    private static string NormalizeSearchValue(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
