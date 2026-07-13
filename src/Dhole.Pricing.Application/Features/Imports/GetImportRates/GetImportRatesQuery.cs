using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRates;

public sealed record GetImportRatesQuery(
    PageRequest Page,
    string? Search = null,
    Guid? ImportBatchId = null,
    ImportSourceType? SourceType = null,
    ImportStatus? Status = null,
    string? Pol = null,
    string? Pod = null,
    string? Carrier = null,
    string? ContainerType = null,
    string? Currency = null,
    DateTime? QuoteDate = null,
    DateTime? ValidFrom = null,
    DateTime? ValidTo = null
) : IQuery<Result<PagedResult<ImportRateDto>>>;
