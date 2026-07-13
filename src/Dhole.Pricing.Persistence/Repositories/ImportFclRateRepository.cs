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
        var query = ApplyFilters(
            dbContext.ImportFclRates.AsNoTracking().Where(x => !x.IsDeleted),
            search: null,
            importBatchId: null,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate,
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
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        DateTime? validFrom = null,
        DateTime? validTo = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.ImportFclRates.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            importBatchId,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate,
            validFrom,
            validTo
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
            .Select(x => new ImportRateDto(
                x.Id,
                x.ImportBatchId,
                x.SourceType.ToString(),
                x.Pol,
                x.Pod,
                x.Carrier,
                x.ContainerType,
                x.Currency,
                x.Freight,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.Status.ToString(),
                x.RawDataJson ?? "{}",
                x.UsedAsRateCount
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<ImportRateDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<ImportRateSelectDto>> GetForSelectAsync(
        string? search = null,
        Guid? importBatchId = null,
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
        var query = ApplyFilters(
            dbContext.ImportFclRates.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            importBatchId,
            sourceType,
            status,
            pol,
            pod,
            carrier,
            containerType,
            currency,
            quoteDate,
            validFrom: null,
            validTo: null
        );

        return await query
            .OrderByDescending(x => x.ValidFrom)
            .ThenBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .Take(100)
            .Select(x => new ImportRateSelectDto(
                x.Id,
                x.ImportBatchId,
                x.SourceType.ToString(),
                x.Pol,
                x.Pod,
                x.Carrier,
                x.ContainerType,
                x.Currency,
                x.Freight,
                x.FreeDays,
                x.ValidFrom,
                x.ValidTo,
                x.RawDataJson ?? "{}",
                x.Status.ToString(),
                x.UsedAsRateCount
            ))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<ImportFclRates> ApplyFilters(
        IQueryable<ImportFclRates> query,
        string? search,
        Guid? importBatchId,
        ImportSourceType? sourceType,
        ImportStatus? status,
        string? pol,
        string? pod,
        string? carrier,
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
                || x.Pod.ToLower().Contains(value)
                || x.Carrier.ToLower().Contains(value)
                || x.ContainerType.ToLower().Contains(value)
                || x.Currency.ToLower().Contains(value)
                || x.SourceType.ToString().ToLower().Contains(value)
                || x.Status.ToString().ToLower().Contains(value)
                || (x.RawDataJson != null && x.RawDataJson.ToLower().Contains(value))
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

        if (!string.IsNullOrWhiteSpace(pol))
        {
            var value = NormalizeSearchValue(pol);

            query = query.Where(x => x.Pol.ToLower().Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(pod))
        {
            var value = NormalizeSearchValue(pod);

            query = query.Where(x => x.Pod.ToLower().Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(carrier))
        {
            var value = NormalizeSearchValue(carrier);

            query = query.Where(x => x.Carrier.ToLower().Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(containerType))
        {
            var value = NormalizeSearchValue(containerType);

            query = query.Where(x => x.ContainerType.ToLower().Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            var value = NormalizeSearchValue(currency);

            query = query.Where(x => x.Currency.ToLower().Contains(value));
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
}
