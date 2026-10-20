using System.Security.Claims;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateEndpoints
{
    public static IEndpointRouteBuilder MapSellerRateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/seller-rates", GetMineAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/{rateId:guid}", GetOneAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/{rateId:guid}/document", DownloadAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/dashboard", GetAdminDashboardAsync)
            .WithTags("Seller dashboards")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        RateCreatorIdentityService creatorIdentityService,
        SellerRateAccessService sellerAccess,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        var linkedIds = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.SellerUserId == currentUserId.Value && x.RateId.HasValue)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => x.RateId!.Value)
            .ToListAsync(cancellationToken);

        // A receipt is a persisted, user-ID-based assignment. Preserve visibility
        // even if the executive display name changes or Auth is unavailable.
        var receivedIds = await db.RateHeaders
            .AsNoTracking()
            .Where(rate => !rate.IsDeleted && rate.SellerReceivedBy == currentUserId.Value)
            .Select(rate => rate.Id)
            .ToArrayAsync(cancellationToken);

        // Rates issued directly by Pricing may not have a linked RateRequest.
        // Auth is only needed to resolve those name-based assignments: an Auth
        // outage must never hide requests or receipts already linked by user ID.
        IReadOnlyCollection<Guid> assignedIds;
        try
        {
            assignedIds = await sellerAccess.AssignedRateIdsAsync(currentUserId.Value, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
            || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            loggerFactory.CreateLogger("SellerRateEndpoints").LogWarning(error,
                "No se pudieron resolver los ejecutivos de Auth para el vendedor {UserId}.",
                currentUserId.Value);

            if (linkedIds.Count == 0 && receivedIds.Length == 0)
            {
                return Results.Problem(
                    detail: "No se pudieron verificar las tarifas asignadas. Intente nuevamente.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            assignedIds = Array.Empty<Guid>();
        }

        var orderedIds = linkedIds.Concat(receivedIds).Concat(assignedIds).Distinct().ToArray();
        if (orderedIds.Length == 0)
            return Results.Ok(Array.Empty<RateDto>());

        var items = new List<RateDto>(orderedIds.Length);
        foreach (var rateId in orderedIds)
        {
            var result = await dispatcher.DispatchAsync(
                new GetRateByIdQuery(rateId),
                cancellationToken
            );

            if (result.IsSuccess && result.Value is not null)
                items.Add(result.Value);
        }

        var enrichedItems = await creatorIdentityService.EnrichAsync(
            items,
            cancellationToken
        );

        return Results.Ok(enrichedItems.Select(ToSellerView).ToArray());
    }
    private static async Task<IResult> GetOneAsync(
        Guid rateId,
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        SellerRateAccessService sellerAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, userId.Value, cancellationToken)) return Results.Forbid();

        var result = await dispatcher.DispatchAsync(new GetRateByIdQuery(rateId), cancellationToken);
        return result.IsSuccess && result.Value is not null
            ? Results.Ok(ToSellerView(result.Value))
            : Results.NotFound();
    }

    private static async Task<IResult> DownloadAsync(
        Guid rateId,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        SellerRateAccessService sellerAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, userId.Value, cancellationToken)) return Results.Forbid();

        var result = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(rateId, null, "pdf"), cancellationToken);
        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : Results.Problem("No fue posible generar el PDF de la cotización.");
    }

    // Sólo el SuperUsuario o quien tiene permiso view-all puede consultar este resumen.
    // Se proyectan datos de cabecera, sin cargar cada tarifa por separado.
    private static async Task<IResult> GetAdminDashboardAsync(
        ServiceDbContext db,
        SellerVisibilityService visibility,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IsAdminViewer(httpContext.User, visibility))
            return Results.Forbid();

        // Global supervisory dashboard: include manual and other quotations
        // even when they were not created from a RateRequest.
        var rows = await db.RateHeaders
            .AsNoTracking()
            .Where(rate => !rate.IsDeleted &&
                !(rate.RateType == Dhole.Pricing.Domain.Rates.Enums.RateType.Tariff &&
                  rate.ClientName != null && rate.ClientName.ToLower().Contains("tarifario")))
            .OrderByDescending(rate => rate.CreatedAtUtc)
            .Select(rate => new
            {
                rate.Id,
                rate.RateCode,
                rate.QuoNumber,
                rate.IdtraNumber,
                rate.ClientName,
                rate.ExecutiveName,
                rate.PolName,
                rate.PoeName,
                rate.PodName,
                rate.ContainerQuantity,
                rate.ContainerTypeName,
                rate.ShipmentMode,
                rate.ValidFrom,
                rate.ValidTo,
                rate.TotalSaleAmount,
                rate.CurrencyName,
                rate.CurrencyCode,
                rate.Status,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(rows.Select(rate => new
        {
            rate.Id,
            rate.RateCode,
            rate.QuoNumber,
            rate.IdtraNumber,
            rate.ClientName,
            rate.ExecutiveName,
            rate.PolName,
            rate.PoeName,
            rate.PodName,
            rate.ContainerQuantity,
            rate.ContainerTypeName,
            shipmentMode = rate.ShipmentMode.ToString(),
            rate.ValidFrom,
            rate.ValidTo,
            rate.TotalSaleAmount,
            rate.CurrencyName,
            rate.CurrencyCode,
            status = rate.Status.ToString(),
        }));
    }

    // Never expose Pricing costs, margins or internal supplier data to a seller.
    // Seller API returns only the commercial information found in their quotation.
    private static object ToSellerView(RateDto rate) => new
    {
        rate.Id,
        rate.RateCode,
        rate.RateName,
        rate.RevisionNumber,
        rate.QuoNumber,
        rate.IdtraNumber,
        rate.ClientName,
        rate.ExecutiveName,
        rate.Status,
        rate.SellerReceivedAtUtc,
        rate.SellerReceivedBy,
        rate.ShipmentMode,
        rate.ContainerQuantity,
        rate.ContainerTypeName,
        rate.PolName,
        rate.PoeName,
        rate.PodName,
        rate.CurrencyName,
        rate.CurrencyCode,
        rate.ValidFrom,
        rate.ValidTo,
        rate.TotalSaleAmount,
        rate.Includes,
        rate.SubjectTo,
        rate.Excludes,
        rate.TransitTime,
        RateDetails = rate.RateDetails
            .Where(detail => detail.SaleAmount > 0m)
            .Select(detail => new
            {
                detail.Id,
                detail.Name,
                detail.Quantity,
                detail.CurrencyCode,
                detail.SaleAmount,
                detail.BillToClient,
            }).ToArray(),
    };

    internal static bool IsAdminViewer(ClaimsPrincipal principal, SellerVisibilityService visibility)
    {
        if (visibility.HasScope(principal, PricingConstants.Scopes.RateRequestViewAll))
            return true;

        return principal.Claims
            .Where(claim => claim.Type == ClaimTypes.Role
                || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split(
                new[] { ' ', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(role => role.Equals("SuperUsuario", StringComparison.OrdinalIgnoreCase)
                || role.Equals("SuperUser", StringComparison.OrdinalIgnoreCase));
    }

}
