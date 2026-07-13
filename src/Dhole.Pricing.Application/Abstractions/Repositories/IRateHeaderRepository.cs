using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IRateHeaderRepository : IRepository<RateHeader, Guid>
{
    Task<RateHeader?> GetByIdWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<RateHeader>> GetValidRateHeadersAsync(
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
    );

    Task<IReadOnlyCollection<RateHeader>> GetPendingApprovalAsync(
        Guid? agentId = null,
        Guid? carrierId = null,
        Guid? polId = null,
        Guid? poeId = null,
        Guid? podId = null,
        Guid? containerTypeId = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<RateDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
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
    );

    Task<IReadOnlyCollection<RateSelectDto>> GetForSelectAsync(
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
    );
}
