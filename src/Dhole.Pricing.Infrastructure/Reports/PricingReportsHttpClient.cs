using System.Net.Http.Json;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Infrastructure.Reports;

public sealed class PricingReportsHttpClient(HttpClient httpClient) : IPricingReportsClient
{
    public Task<GeneratedRateDocumentDto> GenerateAsync(
        string templateCode,
        string format,
        string dataJson,
        string fileName,
        string? sheetName = null,
        CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(
            $"/api/internal/reports/templates/{Uri.EscapeDataString(templateCode)}/generate",
            format,
            dataJson,
            fileName,
            sheetName,
            cancellationToken);

    public async Task<GeneratedRateDocumentDto> GenerateGroupedPricingQuoteAsync(
        string parentQuoteNumber,
        IReadOnlyList<PricingQuoteGroupDocumentItem> alternatives,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/internal/reports/quote-groups/generate",
            new { parentQuoteNumber, alternatives },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Reports respondió HTTP {(int)response.StatusCode}: {detail}",
                null, response.StatusCode);
        }
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"{parentQuoteNumber}.pdf";
        return new GeneratedRateDocumentDto(
            name.Trim('"'),
            response.Content.Headers.ContentType?.ToString() ?? "application/pdf",
            content);
    }

    public Task<GeneratedRateDocumentDto> GenerateTabularAsync(
        string format,
        string dataJson,
        string fileName,
        string? sheetName = null,
        CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(
            "/api/internal/reports/tabular/generate",
            format,
            dataJson,
            fileName,
            sheetName,
            cancellationToken);

    private async Task<GeneratedRateDocumentDto> GenerateCoreAsync(
        string path,
        string format,
        string dataJson,
        string fileName,
        string? sheetName,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            format,
            dataJson,
            fileName,
            sheetName
        };

        using var response = await httpClient.PostAsJsonAsync(
            path,
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Reports respondió HTTP {(int)response.StatusCode}: {detail}",
                null,
                response.StatusCode);
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString()
            ?? "application/octet-stream";
        var responseFileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"{fileName}.{format}";

        return new GeneratedRateDocumentDto(
            responseFileName.Trim('"'),
            contentType,
            content);
    }
}
