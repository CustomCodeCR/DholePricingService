using Microsoft.AspNetCore.Http;

namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed class ExtractImportRateFromFileRequest
{
    public required IFormFile File { get; init; }

    public required string ProfileSlug { get; init; }

    public string? CorrelationId { get; init; }
}
