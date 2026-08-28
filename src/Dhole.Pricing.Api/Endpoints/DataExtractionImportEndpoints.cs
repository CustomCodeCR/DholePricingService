using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Api.Endpoints;

public static class DataExtractionImportEndpoints
{
    public static IEndpointRouteBuilder MapDataExtractionImportEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        app.MapPost("/api/pricing/rate-import-batches/from-extraction", ImportFromExtractionAsync)
            .WithTags("Imported FCL Rates")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> ImportFromExtractionAsync(
        ImportRatesFromExtractionRequest request,
        ExtractAndPersistFclPricingImportService importService,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.PricingImportId == Guid.Empty || request.ExtractionExecutionId == Guid.Empty)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidExtractionImport",
                "La extracción y el lote de Pricing son requeridos.",
                httpContext
            );
        }

        if (
            !Enum.TryParse<ImportSourceType>(
                request.SourceType,
                ignoreCase: true,
                out var sourceType
            ) || !Enum.IsDefined(sourceType)
        )
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidImportSourceType",
                "El origen de la importación no es válido.",
                httpContext
            );
        }

        if (
            request.Response is null
            || request.Response.Rows is null
            || request.Response.Issues is null
        )
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidExtractionPayload",
                "Data Extraction no envió un resultado completo.",
                httpContext
            );
        }

        try
        {
            var enrichedResponse = request.Response with
            {
                Rows = request.Response.Rows
                    .Select(row => row with
                    {
                        RawJson = EnrichRawJson(row.RawJson, request),
                    })
                    .ToArray(),
            };

            var extraction = DataExtractionPricingImportMapper.ToApplicationResult(
                enrichedResponse,
                request.ExtractionExecutionId,
                request.PricingImportId
            );

            var result = await importService.PersistExtractionAsync(
                request.PricingImportId,
                sourceType,
                extraction,
                requestedBy: null,
                cancellationToken: cancellationToken
            );

            if (!result.Success)
            {
                return EndpointResults.BadRequest(
                    result.ErrorCode ?? "Pricing.ExtractionImportFailed",
                    result.ErrorMessage ?? "Data Extraction no pudo completar la importación.",
                    httpContext
                );
            }

            return EndpointResults.Ok(
                new ImportRatesFromExtractionResponse(
                    request.PricingImportId,
                    result.ExtractionExecutionId,
                    result.PersistedRows,
                    result.SkippedRows,
                    result
                        .Issues.Select(x => x.Code)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToArray()
                )
            );
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidExtractionImport",
                exception.Message,
                httpContext
            );
        }
    }

    private static string EnrichRawJson(
        string? rawJson,
        ImportRatesFromExtractionRequest request
    )
    {
        object? extracted = null;
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try
            {
                extracted = JsonSerializer.Deserialize<JsonElement>(rawJson);
            }
            catch (JsonException)
            {
                extracted = rawJson;
            }
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
