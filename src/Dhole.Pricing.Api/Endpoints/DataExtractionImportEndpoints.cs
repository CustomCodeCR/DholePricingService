using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Imports;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Api.Endpoints;

public static class DataExtractionImportEndpoints
{
    private const string ExpectedSourceService = "DholeDataExtractionService";

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
        IConfiguration configuration,
        IHostEnvironment environment,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!IsAuthorized(httpContext, configuration, environment))
        {
            return EndpointResults.Unauthorized(
                "Pricing.DataExtractionUnauthorized",
                "La solicitud interna de Data Extraction no está autorizada.",
                httpContext
            );
        }

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
            var extraction = ToApplicationResult(request);

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

    private static DataExtractionFclPricingResult ToApplicationResult(
        ImportRatesFromExtractionRequest request
    )
    {
        var response = request.Response;

        return new DataExtractionFclPricingResult(
            response.Success,
            response.ExtractionExecutionId ?? request.ExtractionExecutionId,
            request.PricingImportId,
            response.CorrelationId,
            new DataExtractionFclPricingSummary(
                response.Summary.TotalRows,
                response.Summary.ValidRows,
                response.Summary.WarningRows,
                response.Summary.InvalidRows,
                response.Summary.HasIssues
            ),
            response.Rows.Select(ToApplicationRow).ToArray(),
            response.Issues.Select(ToApplicationIssue).ToArray(),
            response.ErrorCode,
            response.ErrorMessage,
            ToApplicationReference(response.ProfileReference)
        );
    }

    private static DataExtractionFclPricingRow ToApplicationRow(ExtractedPricingRowRequest row)
    {
        return new DataExtractionFclPricingRow(
            row.Id,
            row.SourceSheetName,
            row.SourceRowNumber,
            row.OriginPort,
            row.PortOfExit,
            row.DestinationPort,
            row.ContainerType,
            row.Carrier,
            row.Agent,
            row.Commodity,
            row.Currency,
            row.FreeDays,
            row.TransitDays,
            row.ValidFrom,
            row.ValidTo,
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            row.SpaceComment,
            row.Remarks,
            row.Status,
            row.RawJson,
            ToApplicationReference(row.OriginPortReference),
            ToApplicationReference(row.PortOfExitReference),
            ToApplicationReference(row.DestinationPortReference),
            ToApplicationReference(row.ContainerTypeReference),
            ToApplicationReference(row.CarrierReference),
            ToApplicationReference(row.AgentReference),
            ToApplicationReference(row.CurrencyReference)
        );
    }

    private static DataExtractionFclPricingIssue ToApplicationIssue(
        ExtractedPricingIssueRequest issue
    )
    {
        return new DataExtractionFclPricingIssue(
            issue.Id,
            issue.ExtractedPricingRowId,
            issue.Code,
            issue.Message,
            issue.IsBlocking,
            issue.SourceSheetName,
            issue.SourceRowNumber,
            issue.ColumnName,
            issue.RawValue
        );
    }

    private static DataExtractionCatalogReference? ToApplicationReference(
        ExtractedCatalogReferenceRequest? reference
    )
    {
        return reference is null
            ? null
            : new DataExtractionCatalogReference(
                reference.Id,
                reference.CatalogGroupSlug,
                reference.Code,
                reference.Slug,
                reference.Name,
                reference.RawValue
            );
    }

    private static bool IsAuthorized(
        HttpContext context,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        if (
            !context.Request.Headers.TryGetValue("X-Source-Service", out var sourceService)
            || !string.Equals(
                sourceService.ToString(),
                ExpectedSourceService,
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        var expectedApiKey = configuration["Pricing:DataExtractionApiKey"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return environment.IsDevelopment()
                || (
                    context.Connection.RemoteIpAddress is not null
                    && IPAddress.IsLoopback(context.Connection.RemoteIpAddress)
                );
        }

        var headerName = configuration["Pricing:DataExtractionApiKeyHeader"];

        if (string.IsNullOrWhiteSpace(headerName))
        {
            headerName = "X-Api-Key";
        }

        if (!context.Request.Headers.TryGetValue(headerName, out var providedApiKey))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey.Trim());
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey.ToString().Trim());

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
