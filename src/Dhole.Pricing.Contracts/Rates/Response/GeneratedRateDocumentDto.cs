namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record GeneratedRateDocumentDto(
    string FileName,
    string ContentType,
    byte[] Content);
