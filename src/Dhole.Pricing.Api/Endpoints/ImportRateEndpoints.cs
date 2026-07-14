using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.ApproveImportRate;
using Dhole.Pricing.Application.Features.Imports.CreateImportRate;
using Dhole.Pricing.Application.Features.Imports.DeleteImportRate;
using Dhole.Pricing.Application.Features.Imports.ExtractImportRateFromFile;
using Dhole.Pricing.Application.Features.Imports.GetImportRateById;
using Dhole.Pricing.Application.Features.Imports.GetImportRates;
using Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;
using Dhole.Pricing.Application.Features.Imports.RejectImportRate;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Dhole.Pricing.Api.Endpoints;

public static class ImportRateEndpoints
{
    public static IEndpointRouteBuilder MapImportRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/import-rates")
            .WithTags("Imported FCL Rates")
            .RequireAuthorization();

        group
            .MapGet("/", GetImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateView);

        group
            .MapGet("/select", GetImportRatesForSelectAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateView);

        group
            .MapGet("/{importRateId:guid}", GetImportRateByIdAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateView);

        group
            .MapPost("/", CreateImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreate);

        group
            .MapPost("/extract", ExtractImportRateFromFileAsync)
            .DisableAntiforgery()
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreate);

        group
            .MapPost("/approve", ApproveImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateApprove);

        group
            .MapPost("/{importRateId:guid}/approve", ApproveImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateApprove);

        group
            .MapPost("/reject", RejectImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReject);

        group
            .MapPost("/{importRateId:guid}/reject", RejectImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReject);

        group
            .MapDelete("/", DeleteImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateDelete);

        return app;
    }

    private static async Task<IResult> GetImportRatesAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        Guid? importBatchId,
        ImportSourceType? sourceType,
        ImportStatus? status,
        string? agent,
        string? carrier,
        string? pol,
        string? poe,
        string? pod,
        string? containerType,
        string? currency,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetImportRatesQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                importBatchId,
                sourceType,
                status,
                agent,
                carrier,
                pol,
                poe,
                pod,
                containerType,
                currency,
                quoteDate,
                validFrom,
                validTo
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetImportRatesForSelectAsync(
        string? search,
        Guid? importBatchId,
        ImportSourceType? sourceType,
        ImportStatus? status,
        string? agent,
        string? carrier,
        string? pol,
        string? poe,
        string? pod,
        string? containerType,
        string? currency,
        DateTime? quoteDate,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetImportRatesForSelectQuery(
                search,
                importBatchId,
                sourceType,
                status,
                agent,
                carrier,
                pol,
                poe,
                pod,
                containerType,
                currency,
                quoteDate
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetImportRateByIdAsync(
        Guid importRateId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetImportRateByIdQuery(importRateId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateImportRateAsync(
        CreateImportRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.SourceType, out ImportSourceType sourceType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidImportSourceType",
                "El origen de la importación no es válido.",
                httpContext
            );
        }

        try
        {
            var result = await dispatcher.DispatchAsync(
                new CreateImportRateCommand(
                    request.ImportBatchId,
                    request.ExtractionRecordId,
                    sourceType,
                    ToSnapshot(request.Profile),
                    ToSnapshot(request.Pol),
                    ToPoeSnapshot(request.Poe, request.Pod),
                    ToSnapshot(request.Pod),
                    ToSnapshot(request.Carrier),
                    ToSnapshot(request.Agent),
                    ToSnapshot(request.ContainerType),
                    ToSnapshot(request.Currency),
                    request.Commodity,
                    request.OceanFreight,
                    request.OriginCharges,
                    request.DestinationCharges,
                    request.Surcharges,
                    request.TotalCost,
                    request.TotalSale,
                    request.Profit,
                    request.Margin,
                    request.FreeDays,
                    request.TransitDays,
                    request.ValidFrom,
                    request.ValidTo,
                    request.RawDataJson,
                    httpContext.GetCurrentUserId()
                ),
                cancellationToken
            );

            return EndpointResults.FromResult(result, httpContext);
        }
        catch (InvalidOperationException exception)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidImportCatalogSnapshot",
                exception.Message,
                httpContext
            );
        }
    }

    private static async Task<IResult> ExtractImportRateFromFileAsync(
        [FromForm] IFormFile file,
        [FromForm] string profileSlug,
        [FromForm] string? correlationId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (file.Length == 0)
        {
            return EndpointResults.BadRequest(
                "Pricing.ImportFileIsEmpty",
                "El archivo de importación está vacío.",
                httpContext
            );
        }

        if (string.IsNullOrWhiteSpace(profileSlug))
        {
            return EndpointResults.BadRequest(
                "Pricing.ImportProfileRequired",
                "Debe seleccionar un perfil de importación.",
                httpContext
            );
        }

        await using var stream = new MemoryStream();

        await file.CopyToAsync(stream, cancellationToken);

        var result = await dispatcher.DispatchAsync(
            new ExtractImportRateFromFileCommand(
                file.FileName,
                file.ContentType,
                profileSlug.Trim(),
                stream.ToArray(),
                httpContext.GetCurrentUserId(),
                httpContext.User.Identity?.Name,
                correlationId
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApproveImportRatesAsync(
        [FromBody] ApproveImportRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ApproveImportRateCommand(request.Ids, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApproveImportRateAsync(
        Guid importRateId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ApproveImportRateCommand([importRateId], httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RejectImportRatesAsync(
        [FromBody] RejectImportRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new RejectImportRateCommand(
                request.Ids,
                request.Reason,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RejectImportRateAsync(
        Guid importRateId,
        [FromBody] RejectImportRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new RejectImportRateCommand(
                [importRateId],
                request.Reason,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteImportRatesAsync(
        [FromBody] DeleteImportRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteImportRateCommand(request.Ids, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static CatalogSnapshot ToPoeSnapshot(
        ImportCatalogSnapshotRequest? poe,
        ImportCatalogSnapshotRequest pod
    )
    {
        return poe is null
            || poe.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(poe.Name)
            || string.IsNullOrWhiteSpace(poe.Code)
            || string.IsNullOrWhiteSpace(poe.Slug)
            ? ToSnapshot(pod)
            : ToSnapshot(poe);
    }

    private static CatalogSnapshot ToSnapshot(ImportCatalogSnapshotRequest request)
    {
        return CatalogSnapshot.Create(request.Id, request.Name, request.Code, request.Slug);
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
