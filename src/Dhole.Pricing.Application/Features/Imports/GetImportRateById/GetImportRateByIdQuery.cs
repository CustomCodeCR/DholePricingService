using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Imports.Response;

namespace Dhole.Pricing.Application.Features.Imports.GetImportRateById;

public sealed record GetImportRateByIdQuery(Guid Id) : IQuery<Result<ImportRateDto>>;
