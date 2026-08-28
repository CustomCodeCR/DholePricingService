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
            .RateHeaders.Include(x => x.RateDetails).Include(x => x.RateContainers)
            .AsSplitQuery()
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
                .Include(x => x.RateContainers)
                .AsSplitQuery()
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
            query = query.Where(x => x.Status == RateStatus.Open);
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
                .Include(x => x.RateContainers)
                .AsSplitQuery()
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
                x.IncotermId,
                x.IncotermName,
                x.IncotermCode,
                x.PickupAddress,
                x.PickupLatitude,
                x.PickupLongitude,
                x.ContainerQuantity,
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.ClientName,
                x.ExecutiveName,
                x.IdtraNumber,
                x.QuoNumber,
                x.Includes,
                x.SubjectTo,
                x.Excludes,
                x.TransitTime,
                x.RateType.ToString(),
                x.ShipmentMode.ToString(),
                x.TotalPackages,
                x.TotalPallets,
                x.TotalWeightKg,
                x.TotalVolumeCbm,
                x.KgPerCbm,
                x.ChargeableQuantity,
                Array.Empty<RateCargoLineDto>(),
                x.TotalCostAmount,
                x.TotalSaleAmount,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.RequiredApproval,
                x.Status.ToString(),
                x.ClosedReason,
                x.ClosedAtUtc,
                x.ClosedBy,
                x.RateContainers
                    .OrderBy(c => c.ContainerTypeName)
                    .ThenBy(c => c.ContainerTypeCode)
                    .Select(c => new RateContainerDto(
                        c.Id,
                        c.RateHeaderId,
                        c.ContainerTypeId,
                        c.ContainerTypeName,
                        c.ContainerTypeCode,
                        c.Quantity
                    ))
                    .ToList(),
                x.RateDetails.OrderBy(d => d.CostDetailType)
                    .ThenBy(d => d.Name)
                    .Select(d => new RateDetailDto(
                        d.Id,
                        d.RateHeaderId,
                        d.CostId,
                        d.Name,
                        d.CostDetailType.ToString(),
                        d.CostType.ToString(),
                        d.ChargeBasis.ToString(),
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

    public async Task<PricingRateDashboardDto> GetDashboardAsync(
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        DateTime? modifiedFrom = null,
        DateTime? modifiedTo = null,
        DateTime? validityFrom = null,
        DateTime? validityTo = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.RateHeaders.AsNoTracking().Where(x => !x.IsDeleted);

        if (createdFrom.HasValue)
        {
            var value = AsUtcDate(createdFrom.Value);
            query = query.Where(x => x.CreatedAtUtc >= value);
        }

        if (createdTo.HasValue)
        {
            var value = AsUtcDate(createdTo.Value).AddDays(1);
            query = query.Where(x => x.CreatedAtUtc < value);
        }

        if (modifiedFrom.HasValue)
        {
            var value = AsUtcDate(modifiedFrom.Value);
            query = query.Where(x => x.UpdatedAtUtc.HasValue && x.UpdatedAtUtc.Value >= value);
        }

        if (modifiedTo.HasValue)
        {
            var value = AsUtcDate(modifiedTo.Value).AddDays(1);
            query = query.Where(x => x.UpdatedAtUtc.HasValue && x.UpdatedAtUtc.Value < value);
        }

        // La vigencia se filtra por intersección: la tarifa debe estar vigente
        // al menos un día dentro del rango solicitado.
        if (validityFrom.HasValue)
        {
            var value = AsUtcDate(validityFrom.Value);
            query = query.Where(x => x.ValidTo >= value);
        }

        if (validityTo.HasValue)
        {
            var value = AsUtcDate(validityTo.Value).AddDays(1);
            query = query.Where(x => x.ValidFrom < value);
        }

        var totalRates = await query.CountAsync(cancellationToken);

        var statusCounts = await query
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var statuses = Enum.GetValues<RateStatus>()
            .Select(status =>
            {
                var count = statusCounts.GetValueOrDefault(status);
                var percentage = totalRates == 0
                    ? 0m
                    : Math.Round(count * 100m / totalRates, 2, MidpointRounding.AwayFromZero);

                return new PricingRateStatusSummaryDto(status.ToString(), count, percentage);
            })
            .ToList();

        var financialQuery = query.Where(x =>
            x.Status == RateStatus.ApprovedByManagement
            || x.Status == RateStatus.Open
            || x.Status == RateStatus.Sent
            || x.Status == RateStatus.RequestedByClient
            || x.Status == RateStatus.AcceptedByClient
        );

        var financials = await financialQuery
            .GroupBy(x => new
            {
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
            })
            .OrderBy(group => group.Key.CurrencyCode)
            .Select(group => new PricingRateCurrencySummaryDto(
                group.Key.CurrencyId,
                group.Key.CurrencyName,
                group.Key.CurrencyCode,
                group.Count(),
                group.Sum(x => x.TotalCostAmount),
                group.Sum(x => x.TotalSaleAmount),
                group.Sum(x => x.TotalUtilityAmount),
                group.Average(x => x.MarginPercentage)
            ))
            .ToListAsync(cancellationToken);

        var recentRateRows = await query
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .Select(x => new
            {
                x.Id,
                x.RateCode,
                x.RateName,
                x.Status,
                x.ClientName,
                x.CarrierName,
                x.PolName,
                x.PoeName,
                x.PodName,
                x.ContainerTypeName,
                x.CurrencyCode,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ValidFrom,
                x.ValidTo,
            })
            .ToListAsync(cancellationToken);

        var recentRates = recentRateRows
            .Select(x => new PricingRateDashboardItemDto(
                x.Id,
                x.RateCode,
                x.RateName,
                x.Status.ToString(),
                x.ClientName,
                x.CarrierName,
                x.PolName,
                x.PoeName,
                x.PodName,
                x.ContainerTypeName,
                x.CurrencyCode,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ValidFrom,
                x.ValidTo
            ))
            .ToList();

        var lastCreatedAtUtc = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => (DateTime?)x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var lastModifiedAtUtc = await query
            .Where(x => x.UpdatedAtUtc.HasValue)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new PricingRateDashboardDto(
            totalRates,
            statusCounts.GetValueOrDefault(RateStatus.PendingApproval),
            statusCounts.GetValueOrDefault(RateStatus.ApprovedByManagement),
            statusCounts.GetValueOrDefault(RateStatus.RejectedByManagement)
                + statusCounts.GetValueOrDefault(RateStatus.RejectedByClient),
            statusCounts.GetValueOrDefault(RateStatus.Open),
            statusCounts.GetValueOrDefault(RateStatus.Sent),
            statusCounts.GetValueOrDefault(RateStatus.RequestedByClient),
            statusCounts.GetValueOrDefault(RateStatus.AcceptedByClient),
            statusCounts.GetValueOrDefault(RateStatus.Closed),
            statusCounts.GetValueOrDefault(RateStatus.Expired),
            lastCreatedAtUtc,
            lastModifiedAtUtc,
            statuses,
            financials,
            recentRates
        );
    }

    private static DateTime AsUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
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
                || (x.ClosedReason ?? string.Empty).ToLower().Contains(value)
                || (x.AgentName ?? string.Empty).ToLower().Contains(value)
                || (x.AgentCode ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierName ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierCode ?? string.Empty).ToLower().Contains(value)
                || x.PolName.ToLower().Contains(value)
                || x.PolCode.ToLower().Contains(value)
                || x.PoeName.ToLower().Contains(value)
                || x.PoeCode.ToLower().Contains(value)
                || (x.PodName ?? string.Empty).ToLower().Contains(value)
                || (x.PodCode ?? string.Empty).ToLower().Contains(value)
                || x.ContainerTypeName.ToLower().Contains(value)
                || x.ContainerTypeCode.ToLower().Contains(value)
                || x.RateContainers.Any(c =>
                    c.ContainerTypeName.ToLower().Contains(value)
                    || c.ContainerTypeCode.ToLower().Contains(value))
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
            query = query.Where(x =>
                x.ContainerTypeId == containerTypeId.Value
                || x.RateContainers.Any(c => c.ContainerTypeId == containerTypeId.Value)
            );
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
        string? podCode,
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

        var route = string.IsNullOrWhiteSpace(podCode)
            ? $"{polCode} → {poeCode}"
            : string.IsNullOrWhiteSpace(poeCode)
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
