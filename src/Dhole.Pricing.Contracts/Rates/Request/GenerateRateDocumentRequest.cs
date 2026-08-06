namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record GenerateRateDocumentRequest(
    string? TemplateCode = null,
    string Format = "pdf");
