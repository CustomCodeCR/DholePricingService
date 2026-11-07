using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Abstractions.Reports;

public sealed record PricingQuoteGroupDocumentItem(
    string QuoteNumber,
    string Route,
    string Carrier,
    string OfAmount,
    string Currency,
    string DataJson,
    string TemplateCode = "pricing-fcl-client-quote");

public interface IPricingReportsClient
{
    Task<GeneratedRateDocumentDto> GenerateAsync(
        string templateCode,
        string format,
        string dataJson,
        string fileName,
        string? sheetName = null,
        CancellationToken cancellationToken = default);

    Task<GeneratedRateDocumentDto> GenerateGroupedPricingQuoteAsync(
        string parentQuoteNumber,
        IReadOnlyList<PricingQuoteGroupDocumentItem> alternatives,
        CancellationToken cancellationToken = default);

    Task<GeneratedRateDocumentDto> GenerateTabularAsync(
        string format,
        string dataJson,
        string fileName,
        string? sheetName = null,
        CancellationToken cancellationToken = default);
}
