using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IImportFclRateRepository : IRepository<ImportFclRates, Guid>
{
    Task<IReadOnlyCollection<ImportFclRates>> GetByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ImportFclRates>> GetPendingByImportFclBatchIdAsync(
        Guid importBatchId,
        CancellationToken cancellationToken = default
    );

    Task<bool> ExistsCreatedRateFclAsync(
        Guid importFclRateId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ImportFclRates>> GetValidImportedRatesFclAsync(
        ImportSourceType? sourceType = null,
        ImportStatus? status = null,
        string? pol = null,
        string? pod = null,
        string? carrier = null,
        string? containerType = null,
        string? currency = null,
        DateTime? quoteDate = null,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<ImportRateDto>> GetPagedAsync(
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
    );

    Task<IReadOnlyCollection<ImportRateSelectDto>> GetForSelectAsync(
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
    );
}
