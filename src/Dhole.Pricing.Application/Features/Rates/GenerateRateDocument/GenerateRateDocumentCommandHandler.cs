using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;

public sealed class GenerateRateDocumentCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateReportDataFactory dataFactory,
    IPricingReportsClient reportsClient)
    : ICommandHandler<GenerateRateDocumentCommand, Result<GeneratedRateDocumentDto>>
{
    private const string PricingClientQuoteTemplateCode = "pricing-fcl-client-quote";

    public async Task<Result<GeneratedRateDocumentDto>> HandleAsync(
        GenerateRateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var rate = await rateHeaders.GetByIdWithDetailsAsync(command.RateId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.RateHeaderNotFound);

        var format = string.IsNullOrWhiteSpace(command.Format)
            ? "pdf"
            : command.Format.Trim().ToLowerInvariant();

        if (format is not ("pdf" or "xlsx" or "csv"))
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.UnsupportedReportFormat);

        // La impresión de Pricing usa una única plantilla cliente administrada desde Reports.
        // No aceptamos que la UI sustituya el código por otra plantilla accidentalmente.
        var templateCode = PricingClientQuoteTemplateCode;

        var fileName = rate.QuoNumber ?? rate.RateCode;
        var dataJson = dataFactory.CreateDataJson(rate);

        try
        {
            var document = await reportsClient.GenerateAsync(
                templateCode,
                format,
                dataJson,
                fileName,
                "Tarifa",
                cancellationToken);
            return Result.Success(document);
        }
        catch (HttpRequestException)
        {
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.ReportGenerationFailed);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.ReportGenerationTimedOut);
        }
    }
}
