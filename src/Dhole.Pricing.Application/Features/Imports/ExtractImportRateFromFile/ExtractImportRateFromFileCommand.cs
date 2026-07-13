using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Imports;

namespace Dhole.Pricing.Application.Features.Imports.ExtractImportRateFromFile;

public sealed record ExtractImportRateFromFileCommand(
    string OriginalFileName,
    string? ContentType,
    string ProfileSlug,
    byte[] FileContent,
    Guid? RequestedBy,
    string? RequestedByName,
    string? CorrelationId = null
) : ICommand<Result<ExtractAndPersistFclPricingImportResult>>;
