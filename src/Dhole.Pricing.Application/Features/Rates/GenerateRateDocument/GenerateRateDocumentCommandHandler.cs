using System.Text.Json.Nodes;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Enums;
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

        // Los documentos comerciales solo pueden generarse cuando la tarifa ya superó
        // el flujo de aprobación. Esta validación vive en Application para que no pueda
        // omitirse llamando directamente al endpoint de documentos.
        //
        // Open representa una tarifa que no requirió aprobación manual (margen >= mínimo)
        // o que fue autoaprobada por un usuario autorizado. Los estados posteriores también
        // provienen de una tarifa ya habilitada comercialmente.
        if (!CanGenerateCommercialDocument(rate.Status, rate.RequiredApproval))
        {
            var error = rate.RequiredApproval || rate.Status == RateStatus.PendingApproval
                ? PricingErrors.RateLowMarginRequiresApproval
                : PricingErrors.RateInvalidStatus;

            return Result.Failure<GeneratedRateDocumentDto>(error);
        }

        var format = string.IsNullOrWhiteSpace(command.Format)
            ? "pdf"
            : command.Format.Trim().ToLowerInvariant();

        if (format is not ("pdf" or "xlsx" or "csv"))
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.UnsupportedReportFormat);

        // FCL y LCL tienen contratos visuales distintos. LCL no expone naviera ni
        // un contenedor interno de compatibilidad; FCL conserva su plantilla propia.
        var templateCode = rate.ShipmentMode == ShipmentMode.Lcl
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

    private static bool CanGenerateCommercialDocument(RateStatus status, bool requiredApproval)
    {
        if (requiredApproval)
            return false;

        return status is
            RateStatus.ApprovedByManagement
            or RateStatus.Open
            or RateStatus.Sent
            or RateStatus.AcceptedByClient
            or RateStatus.RejectedByClient
            or RateStatus.Closed
            or RateStatus.Expired;
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
