using System.Security.Claims;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Middleware;

public sealed class SellerRateVisibilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ServiceDbContext db)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await next(context);
            return;
        }

        if (!IsRestrictedSeller(context.User))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/pricing/rates", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // El vendedor consume sus tarifas por /api/pricing/seller-rates. Bloquear el browse
        // y el dashboard general evita que pueda enumerar o inferir tarifas de otros vendedores.
        if (string.Equals(path.TrimEnd('/'), "/api/pricing/rates", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/pricing/rates/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ratesIndex = Array.FindIndex(segments, value => string.Equals(value, "rates", StringComparison.OrdinalIgnoreCase));
        if (ratesIndex < 0 || ratesIndex + 1 >= segments.Length || !Guid.TryParse(segments[ratesIndex + 1], out var rateId))
        {
            await next(context);
            return;
        }

        var currentUserId = context.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var ownsRate = await db.RateRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.SellerUserId == currentUserId.Value && request.RateId == rateId,
                context.RequestAborted
            );

        if (!ownsRate)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private static bool IsRestrictedSeller(ClaimsPrincipal user)
    {
        var roleValues = user.Claims
            .Where(claim => claim.Type == ClaimTypes.Role || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase) || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        var sellerRole = roleValues.Any(role => role is "vendedor" or "seller" or "ventas" || role.Contains("vendedor") || role.Contains("seller"));

        var scopes = user.Claims
            .Where(claim => claim.Type.Equals("scp", StringComparison.OrdinalIgnoreCase) || claim.Type.Equals("scope", StringComparison.OrdinalIgnoreCase) || claim.Type.Equals("scopes", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasRequestCreate = HasScope(scopes, PricingConstants.Scopes.RateRequestCreate);
        var hasRateUpdate = HasScope(scopes, PricingConstants.Scopes.RateUpdate);

        return sellerRole || (hasRequestCreate && !hasRateUpdate);
    }

    private static bool HasScope(HashSet<string> scopes, string required)
    {
        var normalized = required.Trim().ToLowerInvariant();
        if (scopes.Contains("*") || scopes.Contains(normalized)) return true;
        return scopes.Any(scope => scope.EndsWith(".*", StringComparison.Ordinal)
            && normalized.StartsWith(scope[..^1], StringComparison.Ordinal));
    }

    private static IEnumerable<string> Split(string value)
        => value.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
