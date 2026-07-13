using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.GetRates;

public sealed record GetRatesQuery(
    PageRequest Page,
    string? Search = null,
    Guid? SourceImportFclRateId = null,
    Guid? AgentId = null,
    Guid? CarrierId = null,
    Guid? PolId = null,
    Guid? PoeId = null,
    Guid? PodId = null,
    Guid? ContainerTypeId = null,
    Guid? CurrencyId = null,
    RateStatus? Status = null,
    bool? RequiredApproval = null,
    DateTime? QuoteDate = null,
    DateTime? ValidFrom = null,
    DateTime? ValidTo = null
) : IQuery<Result<PagedResult<RateDto>>>;
