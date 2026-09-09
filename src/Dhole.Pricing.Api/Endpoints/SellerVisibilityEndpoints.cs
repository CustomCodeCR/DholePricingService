using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerVisibilityEndpoints
{
    public static IEndpointRouteBuilder MapSellerVisibilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/seller-visibility")
            .WithTags("Seller visibility")
            .RequireAuthorization();

        group.MapGet("/me", GetMineAsync);

        group.MapGet("/{viewerUserId:guid}", GetAsync)
            .RequireScope(PricingConstants.Scopes.RateRequestVisibilityManage);

        group.MapPut("/{viewerUserId:guid}", ReplaceAsync)
            .RequireScope(PricingConstants.Scopes.RateRequestVisibilityManage);

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        SellerVisibilityService visibilityService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var visibility = await visibilityService.ResolveAsync(
            httpContext,
            requireElevated: false,
            cancellationToken
        );

        if (visibility is null)
        {
            return Results.Forbid();
        }

        return Results.Ok(new
        {
            viewerUserId = visibility.ViewerUserId,
            mode = visibility.Mode.ToString(),
            sellerUserIds = visibility.SellerUserIds.OrderBy(x => x).ToArray(),
        });
    }

    private static async Task<IResult> GetAsync(
        Guid viewerUserId,
        SellerVisibilityService visibilityService,
        CancellationToken cancellationToken)
    {
        if (viewerUserId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "El usuario supervisor es requerido." });
        }

        var sellerUserIds = await visibilityService.GetAssignedSellerIdsAsync(
            viewerUserId,
            cancellationToken
        );

        return Results.Ok(new
        {
            viewerUserId,
            sellerUserIds,
            ownVisibilityIsImplicit = true,
        });
    }

    private static async Task<IResult> ReplaceAsync(
        Guid viewerUserId,
        ReplaceSellerVisibilityRequest request,
        SellerVisibilityService visibilityService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (viewerUserId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "El usuario supervisor es requerido." });
        }

        var sellerUserIds = request.SellerUserIds?
            .Where(value => value != Guid.Empty && value != viewerUserId)
            .Distinct()
            .ToArray() ?? [];

        if (sellerUserIds.Length > 500)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerVisibilityTooManyAssignments",
                message = "No se pueden asignar más de 500 vendedores a un solo usuario.",
            });
        }

        await visibilityService.ReplaceAssignedSellerIdsAsync(
            viewerUserId,
            sellerUserIds,
            httpContext.GetCurrentUserId(),
            cancellationToken
        );

        return Results.Ok(new
        {
            viewerUserId,
            sellerUserIds,
            ownVisibilityIsImplicit = true,
        });
    }

    private sealed record ReplaceSellerVisibilityRequest(Guid[]? SellerUserIds);
}
