using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Api.Endpoints;

public static class DataExtractionImportEndpoints
{
    public static IEndpointRouteBuilder MapDataExtractionImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/rate-import-batches/from-extraction", ImportFromExtractionAsync)
            .RequireIdempotency()
            .WithTags("Imported FCL Rates").AllowAnonymous();
        app.MapGet("/api/pricing/rate-import-batches/learning-context", GetLearningContextAsync)
            .WithTags("Imported FCL Rates").AllowAnonymous();
        return app;
    }

    private static async Task<IResult> GetLearningContextAsync(
        int? limit,
        IImportRateAiFeedbackStore feedbackStore,
        CancellationToken cancellationToken)
    {
        var examples = await feedbackStore.GetLearningContextAsync(Math.Clamp(limit ?? 12, 1, 50), cancellationToken);

        return Results.Ok(new
        {
            examples = examples.Select(example => new
            {
                // Maintain backward compatibility with the extraction prompt: human verified/corrected
                // examples are positive, explicit rejections are negative.
                outcome = example.Outcome.Equals("rejected", StringComparison.OrdinalIgnoreCase)
                    ? "rejected"
                    : "approved",
                feedbackOutcome = example.Outcome,
                feedbackReasonCodes = example.ReasonCodes,
                feedbackComment = example.Comment,
                correctValue = example.CorrectValue,
                originalSnapshotJson = example.OriginalSnapshotJson,
                reviewedSnapshotJson = example.ReviewedSnapshotJson,
                pol = example.Pol,
                poe = example.Poe,
                pod = example.Pod,
                containerType = example.ContainerType,
                carrier = example.Carrier,
                currency = example.Currency,
                oceanFreight = example.OceanFreight,
                originCharges = example.OriginCharges,
                destinationCharges = example.DestinationCharges,
                surcharges = example.Surcharges,
                totalCost = example.TotalCost,
                freeDays = example.FreeDays,
                transitDays = example.TransitDays,
                validFrom = example.ValidFrom,
                validTo = example.ValidTo,
                commodity = example.Commodity,
                // AdaptivePricingAiExtractionClient already forwards SpaceComment to Qwen. Embed the
                // supervised feedback here too, so older extraction deployments also learn immediately.
                spaceComment = BuildLearningComment(example),
            })
        });
    }

    private static string? BuildLearningComment(PricingLearningFeedback example)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(example.SpaceComment)) parts.Add(example.SpaceComment.Trim());
        parts.Add($"HUMAN_FEEDBACK_OUTCOME={example.Outcome}");
        if (example.ReasonCodes.Count > 0)
            parts.Add($"HUMAN_FEEDBACK_REASONS={string.Join(',', example.ReasonCodes)}");
        if (!string.IsNullOrWhiteSpace(example.Comment))
            parts.Add($"HUMAN_FEEDBACK_COMMENT={example.Comment.Trim()}");
        if (!string.IsNullOrWhiteSpace(example.CorrectValue))
            parts.Add($"HUMAN_CORRECT_VALUE={example.CorrectValue.Trim()}");
        if (example.Outcome.Equals("corrected", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"AI_ORIGINAL={Limit(example.OriginalSnapshotJson, 1800)}");
            parts.Add($"HUMAN_REVIEWED={Limit(example.ReviewedSnapshotJson, 1800)}");
        }
        return string.Join(" | ", parts);
    }

    private static string Limit(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static async Task<IResult> ImportFromExtractionAsync(
        ImportRatesFromExtractionRequest request,
        ExtractAndPersistFclPricingImportService importService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.PricingImportId == Guid.Empty || request.ExtractionExecutionId == Guid.Empty)
            return EndpointResults.BadRequest("Pricing.InvalidExtractionImport", "La extracción y el lote de Pricing son requeridos.", httpContext);

        if (!Enum.TryParse<ImportSourceType>(request.SourceType, true, out var sourceType) || !Enum.IsDefined(sourceType))
            return EndpointResults.BadRequest("Pricing.InvalidImportSourceType", "El origen de la importación no es válido.", httpContext);

        if (request.Response is null || request.Response.Rows is null || request.Response.Issues is null)
            return EndpointResults.BadRequest("Pricing.InvalidExtractionPayload", "Data Extraction no envió un resultado completo.", httpContext);

        try
        {
            var enrichedResponse = request.Response with
            {
                Rows = request.Response.Rows.Select(row => row with
                {
                    RawJson = EnrichRawJson(row.RawJson, request),
                }).ToArray(),
            };

            var extraction = DataExtractionPricingImportMapper.ToApplicationResult(
                enrichedResponse,
                request.ExtractionExecutionId,
                request.PricingImportId);

            var result = await importService.PersistExtractionAsync(
                request.PricingImportId,
                sourceType,
                extraction,
                requestedBy: null,
                cancellationToken: cancellationToken);

            if (!result.Success)
                return EndpointResults.BadRequest(
                    result.ErrorCode ?? "Pricing.ExtractionImportFailed",
                    result.ErrorMessage ?? "Data Extraction no pudo completar la importación.",
                    httpContext);

            return EndpointResults.Ok(new ImportRatesFromExtractionResponse(
                request.PricingImportId,
                result.ExtractionExecutionId,
                result.PersistedRows,
                result.SkippedRows,
                result.Issues.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray()));
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest("Pricing.InvalidExtractionImport", exception.Message, httpContext);
        }
    }

    private static string EnrichRawJson(string? rawJson, ImportRatesFromExtractionRequest request)
    {
        object? extracted = null;
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try { extracted = JsonSerializer.Deserialize<JsonElement>(rawJson); }
            catch (JsonException) { extracted = rawJson; }
        }

        return JsonSerializer.Serialize(new
        {
            _dholeSource = new
            {
                emailMessageId = request.EmailMessageId,
                emailAttachmentId = request.EmailAttachmentId,
                sourceType = request.SourceType,
                fromAddress = request.FromAddress,
                subject = request.Subject,
                originalFileName = request.OriginalFileName,
            },
            extracted,
        });
    }
}
