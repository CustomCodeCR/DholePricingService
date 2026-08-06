using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Abstractions.Reports;

public interface IPricingReportsClient
{
    Task<GeneratedRateDocumentDto> GenerateAsync(
        string templateCode,
        string format,
        string dataJson,
        string fileName,
        string? sheetName = null,
        CancellationToken cancellationToken = default);
}
