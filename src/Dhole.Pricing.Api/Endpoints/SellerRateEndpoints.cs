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

        // Pricing may create and send a rate without a RateRequest. Include only rates
        // assigned to the authenticated executive via the Auth directory.
        var assignedIds = await sellerAccess.AssignedRateIdsAsync(currentUserId.Value, cancellationToken);
        var orderedIds = linkedIds.Concat(assignedIds).Distinct().ToArray();
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

        return Results.Ok(enrichedItems);
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
            ? Results.Ok(result.Value)
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

        var linkedRateIds = db.RateRequests
            .AsNoTracking()
            .Where(request => request.RateId.HasValue)
            .Select(request => request.RateId!.Value)
            .Distinct();

        var rows = await db.RateHeaders
            .AsNoTracking()
            .Where(rate => !rate.IsDeleted && linkedRateIds.Contains(rate.Id))
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
