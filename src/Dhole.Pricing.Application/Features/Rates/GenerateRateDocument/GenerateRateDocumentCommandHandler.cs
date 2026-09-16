using System.Text.Json.Nodes;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;

public sealed class GenerateRateDocumentCommandHandler(
    IRateHeaderRepository rateHeaders,
    IRateCommentStore rateComments,
    IRateReportDataFactory dataFactory,
    IPricingReportsClient reportsClient)
    : ICommandHandler<GenerateRateDocumentCommand, Result<GeneratedRateDocumentDto>>
{
    private const string PricingFclClientQuoteTemplateCode = "pricing-fcl-client-quote";
    private const string PricingLclClientQuoteTemplateCode = "pricing-lcl-client-quote";

    public async Task<Result<GeneratedRateDocumentDto>> HandleAsync(
        GenerateRateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var rate = await rateHeaders.GetByIdWithDetailsAsync(command.RateId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.RateHeaderNotFound);

        // Una tarifa con margen menor al 12% todavía es una solicitud pendiente de
        // autorización mientras RequiredApproval esté activo. Si gerencia la rechazó,
        // RequiredApproval vuelve a false, pero eso no debe habilitar la cotización:
        // únicamente una aprobación real o la autoaprobación por scope puede hacerlo.
        if (
            rate.RequiredApproval
            || rate.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.RejectedByManagement
        )
        {
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.RateLowMarginRequiresApproval);
        }

        var format = string.IsNullOrWhiteSpace(command.Format)
            ? "pdf"
            : command.Format.Trim().ToLowerInvariant();

        if (format is not ("pdf" or "xlsx" or "csv"))
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.UnsupportedReportFormat);

        // FCL y LCL tienen contratos visuales distintos. LCL no expone naviera ni
        // un contenedor interno de compatibilidad; FCL conserva su plantilla propia.
        var templateCode = rate.ShipmentMode == Dhole.Pricing.Domain.Rates.Enums.ShipmentMode.Lcl
            ? PricingLclClientQuoteTemplateCode
            : PricingFclClientQuoteTemplateCode;

        var fileName = rate.QuoNumber ?? rate.RateCode;
        var comments = await rateComments.GetAsync(rate.Id, cancellationToken);
        var dataJson = AddRateComments(dataFactory.CreateDataJson(rate), comments);

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

    private static string AddRateComments(string dataJson, string? comments)
    {
        var root = JsonNode.Parse(dataJson) as JsonObject;
        if (root?["rate"] is not JsonObject rate)
            return dataJson;

        rate["comments"] = string.IsNullOrWhiteSpace(comments) ? string.Empty : comments.Trim();
        return root.ToJsonString();
    }
}
