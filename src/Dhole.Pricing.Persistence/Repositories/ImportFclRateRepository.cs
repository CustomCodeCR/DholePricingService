using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class ImportFclRateRepository(ServiceDbContext dbContext)
    : EfRepository<ImportFclRates, Guid>(dbContext),
        IImportFclRateRepository
{
    public async Task<IReadOnlyCollection<ImportFclRates>> GetByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId && !x.IsDeleted)
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ImportFclRates>> GetPendingByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x =>
                x.ImportBatchId == importBatchId && x.Status == ImportStatus.Pending && !x.IsDeleted
            )
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsCreatedRateFclAsync(
        Guid importFclRateId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.ImportFclRates.AnyAsync(
            x =>
                x.Id == importFclRateId
                && !x.IsDeleted
                && (x.CreatedAsRateHeaderId.HasValue || x.UsedAsRateCount > 0),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<ImportFclRates>> GetValidImportedRatesFclAsync(
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
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidFrom.Date <= today
                    && x.ValidTo.Date >= today
                ),
            search: null,
            importBatchId: null,
            sourceType: sourceType,
            status: status,
            agent: null,
            carrier: carrier,
            pol: pol,
            poe: null,
            pod: pod,
            containerType: containerType,
            currency: currency,
            quoteDate: quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ThenBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<ImportRateDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? agent = null,
        string? carrier = null,
        string? pol = null,
        string? poe = null,
        string? pod = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        DateTime? validFrom = null,
        DateTime? validTo = null,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidFrom.Date >= today
                    && x.ValidTo.Date >= today
                ),
            search,
            importBatchId,
            sourceType,
            status,
            agent,
            carrier,
            pol,
            poe,
            pod,
            containerType,
            currency,
            quoteDate: quoteDate,
            validFrom: validFrom,
            validTo: validTo
        );

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.ValidFrom)
            .ThenBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new ImportRateDto
            {
                Id = x.Id,
                ImportBatchId = x.ImportBatchId,
                ExtractionRecordId = x.ExtractionRecordId,
                SourceType = x.SourceType.ToString(),
                ImportProfileId = x.ImportProfileId,
                ImportProfileName = x.ImportProfileName,
                ImportProfileCode = x.ImportProfileCode,
                ImportProfileSlug = x.ImportProfileSlug,
                PolId = x.PolId,
                Pol = x.PolName,
                PolCode = x.PolCode,
                PolSlug = x.PolSlug,
                PoeId = x.PoeId,
                Poe = x.PoeName,
                PoeCode = x.PoeCode,
                PoeSlug = x.PoeSlug,
                PodId = x.PodId,
                Pod = x.PodName,
                PodCode = x.PodCode,
                PodSlug = x.PodSlug,
                CarrierId = x.CarrierId,
                Carrier = x.CarrierName,
                CarrierCode = x.CarrierCode,
                CarrierSlug = x.CarrierSlug,
                AgentId = x.AgentId,
                Agent = x.AgentName,
                AgentCode = x.AgentCode,
                AgentSlug = x.AgentSlug,
                ContainerTypeId = x.ContainerTypeId,
                ContainerType = x.ContainerTypeName,
                ContainerTypeCode = x.ContainerTypeCode,
                ContainerTypeSlug = x.ContainerTypeSlug,
                CurrencyId = x.CurrencyId,
                Currency = x.CurrencyName,
                CurrencyCode = x.CurrencyCode,
                CurrencySlug = x.CurrencySlug,
                Commodity = x.Commodity,
                Freight = x.OceanFreight ?? x.Freight,
                OceanFreight = x.OceanFreight,
                OriginCharges = x.OriginCharges,
                DestinationCharges = x.DestinationCharges,
                Surcharges = x.Surcharges,
                TotalCost = x.TotalCost,
                TotalSale = x.TotalSale,
                Profit = x.Profit,
                Margin = x.Margin,
                FreeDays = x.FreeDays,
                TransitDays = x.TransitDays,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
                RawDataJson = x.RawDataJson ?? "{}",
                Status = x.Status.ToString(),
                UsedAsRateCount = x.UsedAsRateCount,
                CreatedAsRateHeaderId = x.CreatedAsRateHeaderId,
            })
            .ToListAsync(cancellationToken);

        return PagedResult<ImportRateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<PricingDecisionDashboardDto> GetDecisionDashboardAsync(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? containerType = null,
        CancellationToken cancellationToken = default
    )
    {
        const decimal multimodalLandFreight = 2140m;

        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var startDate = dateFrom?.Date;
        var endDateExclusive = dateTo?.Date.AddDays(1);

        var query = dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.Status != ImportStatus.Rejected
                && x.Status != ImportStatus.Expired
                && x.ValidFrom.Date >= today
                && x.ValidTo.Date >= today
            );

        if (startDate.HasValue)
            query = query.Where(x => x.ValidFrom >= startDate.Value);

        if (endDateExclusive.HasValue)
            query = query.Where(x => x.ValidTo < endDateExclusive.Value);

        if (containerType is not null)
            query = query.Where(x => x.ContainerTypeName.Contains(containerType));

        query = query.Where(x =>
            (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("limon")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("limón")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("moin")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("moín")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("caldera")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("manzanillo")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("colon")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("colón")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("rodman")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("cristobal")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("cristóbal")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("panama")
            || (x.PoeName + " " + x.PoeCode + " " + x.PoeSlug + " " + x.Poe)
                .ToLower()
                .Contains("panamá")
        );

        var importedRates = await query
            .OrderByDescending(x => x.OceanFreight ?? x.Freight)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.ContainerTypeName)
            .ThenBy(x => x.PolName)
            .Select(x => new
            {
                x.Id,
                x.ImportBatchId,
                x.CarrierName,
                OceanFreight = x.OceanFreight ?? x.Freight,
                Currency = x.CurrencyName,
                x.ContainerTypeName,
                x.PolName,
                x.PoeName,
                x.ValidFrom,
                x.ValidTo,
            })
            .ToListAsync(cancellationToken);

        var limonMoinRates = new List<PricingDecisionRateDto>();
        var calderaRates = new List<PricingDecisionRateDto>();
        var multimodalRates = new List<PricingDecisionRateDto>();

        foreach (var rate in importedRates)
        {
            var lane = ResolveDecisionLane(rate.PoeName);
            if (lane is null)
            {
                continue;
            }

            var item = new PricingDecisionRateDto(
                rate.Id,
                rate.CarrierName,
                rate.OceanFreight,
                lane == DecisionLane.Multimodal ? multimodalLandFreight : null,
                rate.Currency,
                rate.ContainerTypeName,
                rate.PolName,
                rate.PoeName,
                rate.ValidFrom,
                rate.ValidTo
            );

            switch (lane)
            {
                case DecisionLane.LimonMoin:
                    limonMoinRates.Add(item);
                    break;
                case DecisionLane.Caldera:
                    calderaRates.Add(item);
                    break;
                case DecisionLane.Multimodal:
                    multimodalRates.Add(item);
                    break;
            }
        }

        var lanes = new PricingDecisionLaneDto[]
        {
            new("limon-moin", "Limón / Moín", limonMoinRates.Count, limonMoinRates),
            new("puerto-caldera", "Puerto Caldera", calderaRates.Count, calderaRates),
            new("multimodal", "Multimodal", multimodalRates.Count, multimodalRates),
        };

        return new PricingDecisionDashboardDto(
            startDate,
            dateTo?.Date,
            lanes.Sum(x => x.TotalOptions),
            lanes
        );
    }

    private static DecisionLane? ResolveDecisionLane(string poe)
    {
        var value = RemoveDiacritics(poe).ToLowerInvariant();

        if (value.Contains("limon") || value.Contains("moin"))
        {
            return DecisionLane.LimonMoin;
        }

        if (value.Contains("caldera"))
        {
            return DecisionLane.Caldera;
        }

        if (
            value.Contains("manzanillo")
            || value.Contains("colon")
            || value.Contains("rodman")
            || value.Contains("cristobal")
            || value.Contains("panama")
        )
        {
            return DecisionLane.Multimodal;
        }

        return null;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark
            )
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private enum DecisionLane
    {
        LimonMoin,
        Caldera,
        Multimodal,
    }

    public async Task<IReadOnlyCollection<ImportRateSelectDto>> GetForSelectAsync(
        string? search = null,
        Guid? importBatchId = null,
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? agent = null,
        string? carrier = null,
        string? pol = null,
        string? poe = null,
        string? pod = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;
        await ExpireOutdatedAsync(today, cancellationToken);

        var query = ApplyFilters(
            dbContext
                .ImportFclRates.AsNoTracking()
                .Where(x =>
                    !x.IsDeleted
                    && x.Status != ImportStatus.Expired
                    && x.ValidFrom.Date <= today
                    && x.ValidTo.Date >= today
                ),
            search,
            importBatchId,
            sourceType,
            status,
            agent,
            carrier,
            pol,
            poe,
            pod,
            containerType,
            currency,
            quoteDate: quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .Take(100)
            .Select(x => new ImportRateSelectDto(
                x.Id,
                x.ImportBatchId,
                x.SourceType.ToString(),
                x.PolName,
                x.PodName,
                x.CarrierName,
                x.ContainerTypeName,
                x.CurrencyName,
                x.OceanFreight ?? x.Freight,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.RawDataJson ?? "{}",
                x.Status.ToString(),
                x.UsedAsRateCount
            ))
            .ToListAsync(cancellationToken);
    }

    private Task<int> ExpireOutdatedAsync(DateTime today, CancellationToken cancellationToken)
    {
        return dbContext
            .ImportFclRates.Where(x =>
                !x.IsDeleted && x.Status != ImportStatus.Expired && x.ValidTo.Date < today.Date
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Status, ImportStatus.Expired)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken
            );
    }

    private static IQueryable<ImportFclRates> ApplyFilters(
        IQueryable<ImportFclRates> query,
        string? search,
        Guid? importBatchId,
        ImportSourceType? sourceType,
        ImportStatus? status,
        string? agent,
        string? carrier,
        string? pol,
        string? poe,
        string? pod,
        string? containerType,
        string? currency,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = NormalizeSearchValue(search);

            query = query.Where(x =>
                x.Pol.ToLower().Contains(value)
                || x.PolName.ToLower().Contains(value)
                || x.Poe.ToLower().Contains(value)
                || x.PoeName.ToLower().Contains(value)
                || x.Pod.ToLower().Contains(value)
                || x.PodName.ToLower().Contains(value)
                || x.Carrier.ToLower().Contains(value)
                || x.CarrierName.ToLower().Contains(value)
                || x.Agent.ToLower().Contains(value)
                || x.AgentName.ToLower().Contains(value)
                || x.ContainerType.ToLower().Contains(value)
                || x.ContainerTypeName.ToLower().Contains(value)
                || x.Currency.ToLower().Contains(value)
                || x.CurrencyName.ToLower().Contains(value)
                || x.SourceType.ToString().ToLower().Contains(value)
                || x.Status.ToString().ToLower().Contains(value)
            );
        }

        if (importBatchId.HasValue)
        {
            query = query.Where(x => x.ImportBatchId == importBatchId.Value);
        }

        if (sourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == sourceType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(agent))
        {
            var (primary, secondary) = ParseFilterValues(agent);

            query = query.Where(x =>
                x.AgentCode.ToLower().Contains(primary)
                || x.AgentName.ToLower().Contains(primary)
                || (secondary != null && x.AgentCode.ToLower().Contains(secondary))
                || (secondary != null && x.AgentName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(carrier))
        {
            var (primary, secondary) = ParseFilterValues(carrier);

            query = query.Where(x =>
                x.CarrierCode.ToLower().Contains(primary)
                || x.CarrierName.ToLower().Contains(primary)
                || (secondary != null && x.CarrierCode.ToLower().Contains(secondary))
                || (secondary != null && x.CarrierName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(pol))
        {
            var (primary, secondary) = ParseFilterValues(pol);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PolCode.ToLower())
                || primary.ToLower().Contains(x.PolName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PolCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PolName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(poe))
        {
            var (primary, secondary) = ParseFilterValues(poe);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PoeCode.ToLower())
                || primary.ToLower().Contains(x.PoeName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PoeCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PoeName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(pod))
        {
            var (primary, secondary) = ParseFilterValues(pod);

            query = query.Where(x =>
                primary.ToLower().Contains(x.PodCode.ToLower())
                || primary.ToLower().Contains(x.PodName.ToLower())
                || (secondary != null && secondary.ToLower().Contains(x.PodCode.ToLower()))
                || (secondary != null && secondary.ToLower().Contains(x.PodName.ToLower()))
            );
        }

        if (!string.IsNullOrWhiteSpace(containerType))
        {
            var (primary, secondary) = ParseFilterValues(containerType);

            query = query.Where(x =>
                x.ContainerTypeCode.ToLower().Contains(primary)
                || x.ContainerTypeName.ToLower().Contains(primary)
                || (secondary != null && x.ContainerTypeCode.ToLower().Contains(secondary))
                || (secondary != null && x.ContainerTypeName.ToLower().Contains(secondary))
            );
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            var (primary, secondary) = ParseFilterValues(currency);

            query = query.Where(x =>
                x.CurrencyCode.ToLower().Contains(primary)
                || x.CurrencyName.ToLower().Contains(primary)
                || (secondary != null && x.CurrencyCode.ToLower().Contains(secondary))
                || (secondary != null && x.CurrencyName.ToLower().Contains(secondary))
            );
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

    private static string NormalizeSearchValue(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static (string Primary, string? Secondary) ParseFilterValues(string value)
    {
        var values = value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSearchValue)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        var primary = values.FirstOrDefault() ?? NormalizeSearchValue(value);

        return (primary, values.Length > 1 ? values[1] : null);
    }
}
