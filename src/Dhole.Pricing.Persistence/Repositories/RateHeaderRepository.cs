using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
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
            .RateHeaders.Include(x => x.RateDetails).Include(x => x.RateContainers).Include(x => x.RateServices)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
    }

    public async Task<decimal> GetAcceptedOwnLclCbmAsync(
        Guid consolidationId,
        int consolidationNumber,
        CancellationToken cancellationToken = default
    )
    {
        var target = await LoadOwnLclConsolidationAsync(
            consolidationId,
            consolidationNumber,
            cancellationToken
        );
        if (target is null)
            return 0m;

        var acceptedRates = await dbContext
            .RateHeaders.AsNoTracking()
            .Include(x => x.RateDetails)
            .AsSplitQuery()
            .Where(x =>
                !x.IsDeleted
                && x.ShipmentMode == ShipmentMode.Lcl
                && x.Status == RateStatus.AcceptedByClient
            )
            .ToListAsync(cancellationToken);

        decimal approvedCbm = 0m;

        foreach (var rate in acceptedRates)
        {
            var explicitSource = ResolveOwnLclSource(rate);
            if (explicitSource is not null)
            {
                var matchesById = explicitSource.Value.ConsolidationId == target.Value.Id;
                var matchesByNumber =
                    explicitSource.Value.ConsolidationNumber == target.Value.Number;

                if (matchesById || matchesByNumber)
                    approvedCbm += ResolveOwnLclBillableCbm(rate);

                continue;
            }

            if (!IsLegacyOwnLclRate(rate))
                continue;

            var inferredSource = await ResolveUniqueLegacyOwnLclConsolidationAsync(
                rate,
                cancellationToken
            );

            if (inferredSource?.Id == target.Value.Id)
                approvedCbm += ResolveOwnLclBillableCbm(rate);
        }

        return approvedCbm;
    }

    public async Task<OwnLclCapacitySnapshot?> GetOwnLclCapacityForRateAsync(
        Guid rateId,
        CancellationToken cancellationToken = default
    )
    {
        var rate = await dbContext
            .RateHeaders.AsNoTracking()
            .Include(x => x.RateDetails)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

        if (rate is null || rate.ShipmentMode != ShipmentMode.Lcl)
            return null;

        var source = ResolveOwnLclSource(rate);
        var consolidation = source is not null
            ? await LoadOwnLclConsolidationAsync(
                source.Value.ConsolidationId,
                source.Value.ConsolidationNumber,
                cancellationToken
            )
            : IsLegacyOwnLclRate(rate)
                ? await ResolveUniqueLegacyOwnLclConsolidationAsync(rate, cancellationToken)
                : null;
        if (consolidation is null)
            return null;

        var approvedCbm = await GetAcceptedOwnLclCbmAsync(
            consolidation.Value.Id,
            consolidation.Value.Number,
            cancellationToken
        );
        var remainingCbm = Math.Max(0m, consolidation.Value.MaximumCbm - approvedCbm);
        var requestedCbm = ResolveOwnLclBillableCbm(rate);

        return new OwnLclCapacitySnapshot(
            consolidation.Value.Id,
            consolidation.Value.Number,
            consolidation.Value.MaximumCbm,
            approvedCbm,
            remainingCbm,
            requestedCbm
        );
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
                .Include(x => x.RateServices)
                .AsSplitQuery()
                .Where(x => !x.IsDeleted),
            search: null,
            idtraNumber: null,
            quoNumber: null,
            sourceImportFclRateId: null,
            sourceTariffRateId: null,
            rateType: null,
            tariffMasterOnly: null,
            excludeTariffMasters: null,
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
                .Include(x => x.RateServices)
                .AsSplitQuery()
                .Where(x => !x.IsDeleted),
            search: null,
            idtraNumber: null,
            quoNumber: null,
            sourceImportFclRateId: null,
            sourceTariffRateId: null,
            rateType: null,
            tariffMasterOnly: null,
            excludeTariffMasters: null,
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
        Guid? sourceTariffRateId = null,
        RateType? rateType = null,
        bool? tariffMasterOnly = null,
        bool? excludeTariffMasters = null,
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
            sourceTariffRateId,
            rateType,
            tariffMasterOnly,
            excludeTariffMasters,
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
                x.RevisionNumber,
                x.SourceImportFclRateId,
                x.SourceTariffRateId,
                x.SourceTariffRevisionNumber,
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
                x.WarehouseId,
                x.PickupAddress,
                x.PickupLatitude,
                x.PickupLongitude,
                x.ContainerQuantity,
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.ExchangeRatePurchase,
                x.ExchangeRateSale,
                x.ExchangeRateApplied,
                x.ExchangeRateDate,
                x.ExchangeRateCapturedAtUtc,
                x.ExchangeRateSource,
                x.ExchangeRateManualOverride,
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
                x.UseAllInPresentation,
                x.RateType.ToString(),
                x.ShipmentMode.ToString(),
                x.OperationType.ToString(),
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
                x.TotalCostUsd,
                x.TotalSaleUsd,
                x.TotalUtilityUsd,
                x.TotalCostCrc,
                x.TotalSaleCrc,
                x.TotalUtilityCrc,
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
                        d.Notes,
                        d.ApplyDestinationTax,
                        d.DestinationTaxRate,
                        d.DestinationTaxAmount,
                        d.BillToClient
                    ))
                    .ToList(),
                x.RateServices
                    .OrderBy(s => s.ServiceName)
                    .Select(s => new RateServiceDto(s.ServiceId, s.ServiceName, s.ServiceCode))
                    .ToList()
            )
            {
                CreatedByUserId = x.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        // RateDetails are the source of truth. Recalculate the read snapshot so historical
        // rates created before aggregate USD/CRC fields were populated never render as 0.00.
        items = items.Select(item => item.WithRecalculatedFinancials()).ToList();

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
            sourceTariffRateId: null,
            rateType: null,
            tariffMasterOnly: null,
            excludeTariffMasters: null,
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
        var query = dbContext.RateHeaders
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && !(x.RateType == RateType.Tariff
                    && x.ClientName != null
                    && x.ClientName.ToLower().Contains("tarifario"))
            );

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

        var openCount =
            statusCounts.GetValueOrDefault(RateStatus.PendingApproval)
            + statusCounts.GetValueOrDefault(RateStatus.ApprovedByManagement)
            + statusCounts.GetValueOrDefault(RateStatus.RejectedByManagement)
            + statusCounts.GetValueOrDefault(RateStatus.Open)
            + statusCounts.GetValueOrDefault(RateStatus.RequestedByClient);
        var sentCount = statusCounts.GetValueOrDefault(RateStatus.Sent);
        var expiredCount = statusCounts.GetValueOrDefault(RateStatus.Expired);
        var acceptedCount = statusCounts.GetValueOrDefault(RateStatus.AcceptedByClient);
        var notAcceptedCount =
            statusCounts.GetValueOrDefault(RateStatus.RejectedByClient)
            + statusCounts.GetValueOrDefault(RateStatus.Closed);

        var commercialStatusCounts = new[]
        {
            (Status: RateStatus.Open, Count: openCount),
            (Status: RateStatus.Sent, Count: sentCount),
            (Status: RateStatus.Expired, Count: expiredCount),
            (Status: RateStatus.AcceptedByClient, Count: acceptedCount),
            (Status: RateStatus.RejectedByClient, Count: notAcceptedCount),
        };

        var statuses = commercialStatusCounts
            .Select(item =>
            {
                var percentage = totalRates == 0
                    ? 0m
                    : Math.Round(item.Count * 100m / totalRates, 2, MidpointRounding.AwayFromZero);

                return new PricingRateStatusSummaryDto(item.Status.ToString(), item.Count, percentage);
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
                x.ShipmentMode,
                x.CurrencyCode,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ValidFrom,
                x.ValidTo,
                x.CreatedBy,
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
                x.ShipmentMode == ShipmentMode.Lcl ? "LCL" : x.ContainerTypeName,
                x.CurrencyCode,
                x.TotalUtilityAmount,
                x.MarginPercentage,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ValidFrom,
                x.ValidTo
            )
            {
                CreatedByUserId = x.CreatedBy,
            })
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
            notAcceptedCount,
            openCount,
            sentCount,
            statusCounts.GetValueOrDefault(RateStatus.RequestedByClient),
            acceptedCount,
            statusCounts.GetValueOrDefault(RateStatus.Closed),
            expiredCount,
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
        Guid? sourceTariffRateId,
        RateType? rateType,
        bool? tariffMasterOnly,
        bool? excludeTariffMasters,
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

        if (sourceTariffRateId.HasValue)
        {
            query = query.Where(x => x.SourceTariffRateId == sourceTariffRateId.Value);
        }

        if (rateType.HasValue)
        {
            query = query.Where(x => x.RateType == rateType.Value);
        }

        if (tariffMasterOnly == true)
        {
            query = query.Where(x => x.RateType == RateType.Tariff
                    && x.ClientName != null
                    && x.ClientName.ToLower().Contains("tarifario"));
        }

        if (excludeTariffMasters == true)
        {
            query = query.Where(x => !(x.RateType == RateType.Tariff
                    && x.ClientName != null
                    && x.ClientName.ToLower().Contains("tarifario")));
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
            query = status.Value switch
            {
                RateStatus.Open => query.Where(x =>
                    x.Status == RateStatus.Open
                    || x.Status == RateStatus.PendingApproval
                    || x.Status == RateStatus.ApprovedByManagement
                    || x.Status == RateStatus.RejectedByManagement
                    || x.Status == RateStatus.RequestedByClient
                ),
                RateStatus.RejectedByClient => query.Where(x =>
                    x.Status == RateStatus.RejectedByClient || x.Status == RateStatus.Closed
                ),
                _ => query.Where(x => x.Status == status.Value),
            };
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

    private static readonly Regex OwnLclIdRegex = new(
        @"ConsolidadoId:\s*(?<id>[0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex OwnLclNumberRegex = new(
        @"Consolidado:\s*#(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static string BuildOwnLclIdMarker(Guid consolidationId) =>
        $"ConsolidadoId: {consolidationId:D}";

    private static string BuildOwnLclNumberMarker(int consolidationNumber) =>
        $"Consolidado: #{consolidationNumber} ·";

    private static decimal ResolveOwnLclBillableCbm(RateHeader rate)
    {
        var quantity = rate.RateDetails
            .Where(detail =>
                detail.CostDetailType == CostDetailType.Freight
                && detail.ChargeBasis is ChargeBasis.PerCbm or ChargeBasis.PerChargeableCbm
            )
            .Select(detail => detail.Quantity)
            .DefaultIfEmpty(0m)
            .Max();

        var billableCbm = quantity > 0m ? quantity : Math.Max(0m, rate.ChargeableQuantity);

        // Legacy LCL rates may have persisted the physical CBM (for example 0.20)
        // instead of the commercial minimum. Capacity consumption follows the same
        // 1 CBM minimum used by current LCL quotations.
        return billableCbm > 0m ? Math.Max(1m, billableCbm) : 0m;
    }

    private static bool IsLegacyOwnLclRate(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl)
            return false;

        if (string.Equals(rate.AgentCode, "GCF", StringComparison.OrdinalIgnoreCase))
            return true;

        return (rate.AgentName ?? string.Empty)
            .Contains("Grupo Castro Fallas", StringComparison.OrdinalIgnoreCase);
    }

    private static (Guid? ConsolidationId, int? ConsolidationNumber)? ResolveOwnLclSource(
        RateHeader rate
    )
    {
        foreach (var notes in rate.RateDetails
                     .Select(detail => detail.Notes)
                     .Where(notes => !string.IsNullOrWhiteSpace(notes)))
        {
            var idMatch = OwnLclIdRegex.Match(notes!);
            Guid? id = null;
            if (idMatch.Success && Guid.TryParse(idMatch.Groups["id"].Value, out var parsedId))
                id = parsedId;

            var numberMatch = OwnLclNumberRegex.Match(notes!);
            int? number = null;
            if (numberMatch.Success
                && int.TryParse(numberMatch.Groups["number"].Value, out var parsedNumber))
                number = parsedNumber;

            if (id.HasValue || number.HasValue)
                return (id, number);
        }

        return null;
    }

    private async Task<(Guid Id, int Number, decimal MaximumCbm, Guid? CarrierId, string? CarrierCode, DateOnly? Etd)?> LoadOwnLclConsolidationAsync(
        Guid? consolidationId,
        int? consolidationNumber,
        CancellationToken cancellationToken
    )
    {
        if (!consolidationId.HasValue && !consolidationNumber.HasValue)
            return null;

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            if (consolidationId.HasValue)
            {
                command.CommandText = """
                    SELECT id, consolidation_number, maximum_cbm, carrier_id, carrier_code, etd
                    FROM pricing."OwnLclConsolidations"
                    WHERE id = @source AND is_active = TRUE
                    LIMIT 1;
                    """;
                AddDbParameter(command, "source", consolidationId.Value);
            }
            else
            {
                command.CommandText = """
                    SELECT id, consolidation_number, maximum_cbm, carrier_id, carrier_code, etd
                    FROM pricing."OwnLclConsolidations"
                    WHERE consolidation_number = @source AND is_active = TRUE
                    LIMIT 1;
                    """;
                AddDbParameter(command, "source", consolidationNumber!.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return (
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5)
            );
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<(Guid Id, int Number, decimal MaximumCbm, Guid? CarrierId, string? CarrierCode, DateOnly? Etd)?> ResolveUniqueLegacyOwnLclConsolidationAsync(
        RateHeader rate,
        CancellationToken cancellationToken
    )
    {
        if (!IsLegacyOwnLclRate(rate))
            return null;

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, consolidation_number, maximum_cbm, carrier_id, carrier_code, etd
                FROM pricing."OwnLclConsolidations"
                WHERE is_active = TRUE
                  AND LOWER(status) <> 'closed'
                  AND (
                        (@carrier_id IS NOT NULL AND carrier_id = @carrier_id)
                     OR (@carrier_code <> '' AND UPPER(COALESCE(carrier_code, '')) = UPPER(@carrier_code))
                  )
                  AND (etd IS NULL OR etd > @quote_date)
                ORDER BY etd NULLS LAST, consolidation_number;
                """;

            AddDbParameter(command, "carrier_id", (object?)rate.CarrierId ?? DBNull.Value);
            AddDbParameter(command, "carrier_code", rate.CarrierCode?.Trim() ?? string.Empty);
            AddDbParameter(command, "quote_date", DateOnly.FromDateTime(rate.ValidFrom));

            var matches = new List<(Guid Id, int Number, decimal MaximumCbm, Guid? CarrierId, string? CarrierCode, DateOnly? Etd)>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add((
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5)
                ));
            }

            return matches.Count == 1 ? matches[0] : null;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static void AddDbParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value;
        command.Parameters.Add(parameter);
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
