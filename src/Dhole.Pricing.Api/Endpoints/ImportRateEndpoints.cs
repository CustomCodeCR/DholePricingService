using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Imports.ApproveImportRate;
using Dhole.Pricing.Application.Features.Imports.AssignImportRatePoe;
using Dhole.Pricing.Application.Features.Imports.CreateImportRate;
using Dhole.Pricing.Application.Features.Imports.DeleteImportRate;
using Dhole.Pricing.Application.Features.Imports.ExtractImportRateFromFile;
using Dhole.Pricing.Application.Features.Imports.GetImportRateById;
using Dhole.Pricing.Application.Features.Imports.GetImportRates;
using Dhole.Pricing.Application.Features.Imports.GetImportRatesForSelect;
using Dhole.Pricing.Application.Features.Imports.GetPricingDecisionDashboard;
using Dhole.Pricing.Application.Features.Imports.RejectImportRate;
using Dhole.Pricing.Application.Features.Imports.ReviewImportRate;
using Dhole.Pricing.Contracts.Imports.Request;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Imports.Services;
using Dhole.Pricing.Domain.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Application.Imports;

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
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapGet("/select", GetImportRatesForSelectAsync)
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        group
            .MapGet("/decision-dashboard", GetPricingDecisionDashboardAsync)
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        group
            .MapGet("/{importRateId:guid}", GetImportRateByIdAsync)
            // Pricing necesita releer inmediatamente una fuente creada manualmente desde
            // Pantalla 6. La revisión/aprobación sigue protegida por ImportFclRateReview,
            // pero consultar una fuente individual es parte normal del workspace.
            .RequireScope(PricingConstants.Scopes.WorkspaceAccess);

        group
            .MapPost("/", CreateImportRateAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreate);

        group
            .MapGet("/extract/jobs/{jobId:guid}", GetExtractImportJobAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreate);

        group
            .MapPost("/extract", ExtractImportRateFromFileAsync)
            .RequireIdempotency()
            .DisableAntiforgery()
            .RequireScope(PricingConstants.Scopes.ImportFclRateCreate);

        group
            .MapPost("/approve", ApproveImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPost("/{importRateId:guid}/approve", ApproveImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPut("/{importRateId:guid}/poe", AssignImportRatePoeAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPut("/{importRateId:guid}/review", ReviewImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPost("/reject", RejectImportRatesAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

        group
            .MapPost("/{importRateId:guid}/reject", RejectImportRateAsync)
            .RequireScope(PricingConstants.Scopes.ImportFclRateReview);

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

    private static async Task<IResult> GetPricingDecisionDashboardAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        string? containerType,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value.Date > dateTo.Value.Date)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidDecisionDateRange",
                "La fecha desde no puede ser mayor que la fecha hasta.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new GetPricingDecisionDashboardQuery(dateFrom?.Date, dateTo?.Date, containerType!),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
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
        ImportedShipmentMode? shipmentMode,
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
                quoteDate,
                shipmentMode
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
                    request.SpaceComment,
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

    private const long MaxManualImportFileBytes = 20L * 1024 * 1024;

    private static async Task<IResult> ExtractImportRateFromFileAsync(
        [FromForm] IFormFile file,
        [FromForm] string? profileSlug,
        [FromForm] string? correlationId,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var requestedBy = httpContext.GetCurrentUserId();
        if (!requestedBy.HasValue || requestedBy.Value == Guid.Empty)
            return Results.Unauthorized();

        if (file.Length == 0)
            return EndpointResults.BadRequest(
                "Pricing.ImportFileIsEmpty", "El archivo de importación está vacío.", httpContext);
        if (file.Length > MaxManualImportFileBytes)
            return EndpointResults.BadRequest(
                "Pricing.ImportFileTooLarge", "El archivo supera el máximo de 20 MB.", httpContext);

        var requestKey = httpContext.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (requestKey.Length > 150)
            return EndpointResults.BadRequest(
                "Pricing.InvalidIdempotencyKey", "Idempotency-Key supera 150 caracteres.", httpContext);

        if (requestKey.Length > 0)
        {
            var previous = await dbContext.ManualImportExtractionJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequestedBy == requestedBy.Value &&
                    x.ClientRequestKey == requestKey, cancellationToken);
            if (previous is not null)
                return Results.Accepted($"/api/pricing/import-rates/extract/jobs/{previous.Id}",
                    new { jobId = previous.Id, status = previous.Status });
        }

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        if (memory.Length > MaxManualImportFileBytes)
            return EndpointResults.BadRequest(
                "Pricing.ImportFileTooLarge", "El archivo supera el máximo de 20 MB.", httpContext);

        var job = new ManualImportExtractionJob
        {
            Id = Guid.NewGuid(),
            RequestedBy = requestedBy.Value,
            RequestedByName = httpContext.User.Identity?.Name,
            ClientRequestKey = requestKey.Length == 0 ? null : requestKey,
            OriginalFileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            ProfileSlug = string.IsNullOrWhiteSpace(profileSlug) ? null : profileSlug.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N") : correlationId.Trim(),
            FileContent = memory.ToArray(),
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.ManualImportExtractionJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (requestKey.Length > 0)
        {
            // Concurrent requests with the same idempotency key: report the winner.
            dbContext.Entry(job).State = EntityState.Detached;
            var previous = await dbContext.ManualImportExtractionJobs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequestedBy == requestedBy.Value &&
                    x.ClientRequestKey == requestKey, cancellationToken);
            if (previous is null) throw;
            return Results.Accepted($"/api/pricing/import-rates/extract/jobs/{previous.Id}",
                new { jobId = previous.Id, status = previous.Status });
        }

        return Results.Accepted($"/api/pricing/import-rates/extract/jobs/{job.Id}",
            new { jobId = job.Id, status = job.Status });
    }

    private static async Task<IResult> GetExtractImportJobAsync(
        Guid jobId,
        ServiceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var requestedBy = httpContext.GetCurrentUserId();
        if (!requestedBy.HasValue || requestedBy.Value == Guid.Empty)
            return Results.Unauthorized();

        var job = await dbContext.ManualImportExtractionJobs
            .AsNoTracking()
            .Where(x => x.Id == jobId && x.RequestedBy == requestedBy.Value)
            .Select(x => new
            {
                x.Id, x.Status, x.AttemptCount, x.CreatedAtUtc,
                x.CompletedAtUtc, x.ErrorMessage, x.ResultJson
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null) return Results.NotFound();

        return Results.Ok(new
        {
            jobId = job.Id,
            job.Status,
            job.AttemptCount,
            job.CreatedAtUtc,
            job.CompletedAtUtc,
            job.ErrorMessage,
            extractionResult = job.ResultJson is null
                ? null
                : JsonSerializer.Deserialize<ExtractAndPersistFclPricingImportResult>(job.ResultJson)
        });
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

    private static async Task<IResult> AssignImportRatePoeAsync(
        Guid importRateId,
        [FromBody] AssignImportRatePoeRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new AssignImportRatePoeCommand(
                importRateId,
                request.PoeId,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ReviewImportRateAsync(
        Guid importRateId,
        [FromBody] ReviewImportRateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ReviewImportRateCommand(
                importRateId,
                request.ImportProfileId,
                request.ShipmentMode,
                request.PolId,
                request.PoeId,
                request.PodId,
                request.CarrierId,
                request.AgentId,
                request.ContainerTypeId,
                request.CurrencyId,
                request.Commodity,
                request.SpaceComment,
                request.OceanFreight,
                request.OriginCharges,
                request.DestinationCharges,
                request.Surcharges,
                request.TotalSale,
                request.FreeDays,
                request.TransitDays,
                request.ValidFrom,
                request.ValidTo,
                request.ReviewNotes,
                httpContext.GetCurrentUserId()
            ),
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
        return
            poe is null
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
