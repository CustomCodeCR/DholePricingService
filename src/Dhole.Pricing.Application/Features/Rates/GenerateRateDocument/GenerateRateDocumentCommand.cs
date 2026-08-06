using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;

public sealed record GenerateRateDocumentCommand(
    Guid RateId,
    string? TemplateCode,
    string Format) : ICommand<Result<GeneratedRateDocumentDto>>;
