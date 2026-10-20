using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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
    IPricingReportsClient reportsClient,
    ILogger<GenerateRateDocumentCommandHandler> logger)
    : ICommandHandler<GenerateRateDocumentCommand, Result<GeneratedRateDocumentDto>>
{
    private const string PricingFclClientQuoteTemplateCode = "pricing-fcl-client-quote";
    private const string PricingLclClientQuoteTemplateCode = "pricing-lcl-client-quote";

    public async Task<Result<GeneratedRateDocumentDto>> HandleAsync(
        GenerateRateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var stage = "load-rate";
        try
        {
            var rate = await rateHeaders.GetByIdWithDetailsAsync(command.RateId, cancellationToken);
            if (rate is null || rate.IsDeleted)
                return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.RateHeaderNotFound);

            // La cotización comercial requiere estar habilitada para su emisión.
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

            var templateCode = rate.ShipmentMode == ShipmentMode.Lcl
                ? PricingLclClientQuoteTemplateCode
                : PricingFclClientQuoteTemplateCode;

            stage = "build-payload";
            var fileName = rate.QuoNumber ?? rate.RateCode;
            var comments = await rateComments.GetAsync(rate.Id, cancellationToken);
            var dataJson = AddRateComments(dataFactory.CreateDataJson(rate), comments);

            stage = "call-reports-service";
            var document = await reportsClient.GenerateAsync(
                templateCode, format, dataJson, fileName, "Tarifa", cancellationToken);
            return Result.Success(document);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception,
                "Reports Service rechazó el documento de tarifa {RateId} en {Stage}",
                command.RateId, stage);
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.ReportGenerationFailed);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception,
                "Reports Service agotó el tiempo al generar la tarifa {RateId} en {Stage}",
                command.RateId, stage);
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.ReportGenerationTimedOut);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Log exception and stage, never the private commercial report payload.
            logger.LogError(exception,
                "Falló la generación del documento para tarifa {RateId} en {Stage}",
                command.RateId, stage);
            return Result.Failure<GeneratedRateDocumentDto>(PricingErrors.ReportGenerationFailed);
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
