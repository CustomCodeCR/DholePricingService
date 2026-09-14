using System.Security.Claims;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public sealed record UpdateRateCommentsRequest(string? Comments);

public static class RateCommentEndpoints
{
    public static IEndpointRouteBuilder MapRateCommentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates")
            .WithTags("Rates")
            .RequireAuthorization();

        group.MapGet("/{rateId:guid}/comments", GetCommentsAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group.MapPut("/{rateId:guid}/comments", SetCommentsAsync);

        return app;
    }

    private static async Task<IResult> GetCommentsAsync(
        Guid rateId,
        IRateCommentStore comments,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (!await RateExistsAsync(db, rateId, cancellationToken))
            return Results.NotFound();

        var value = await comments.GetAsync(rateId, cancellationToken);
        return Results.Ok(new { comments = value ?? string.Empty });
    }

    private static async Task<IResult> SetCommentsAsync(
        Guid rateId,
        UpdateRateCommentsRequest request,
        IRateCommentStore comments,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!HasScope(httpContext.User, PricingConstants.Scopes.RateCreate)
            && !HasScope(httpContext.User, PricingConstants.Scopes.RateUpdate))
        {
            return Results.Forbid();
        }

        if (!await RateExistsAsync(db, rateId, cancellationToken))
            return Results.NotFound();

        var value = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim();
        if (value?.Length > 4000)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.RateCommentsTooLong",
                message = "Los comentarios de la tarifa no pueden superar 4000 caracteres."
            });
        }

        await comments.SetAsync(rateId, value, ResolveUserId(httpContext.User), cancellationToken);
        return Results.NoContent();
    }

    private static Task<bool> RateExistsAsync(
        ServiceDbContext db,
        Guid rateId,
        CancellationToken cancellationToken
    ) => db.RateHeaders.AsNoTracking().AnyAsync(
        rate => rate.Id == rateId && !rate.IsDeleted,
        cancellationToken
    );

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        return user.Claims
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue("user_id");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
