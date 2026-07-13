using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;

public sealed record GetImportRatesForSelectQuery(
    string? Search = null,
    Guid? ImportBatchId = null,
    ImportSourceType? SourceType = null,
    ImportStatus? Status = null,
    string? Agent = null,
    string? Carrier = null,
    string? Pol = null,
    string? Poe = null,
    string? Pod = null,
    string? ContainerType = null,
    string? Currency = null,
    DateTime? QuoteDate = null
) : IQuery<Result<IReadOnlyCollection<ImportRateSelectDto>>>;
