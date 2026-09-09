using System.Security.Claims;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Middleware;

public sealed class SellerRateVisibilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        SellerVisibilityService visibilityService)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await next(context);
            return;
        }

        if (!IsRestrictedSeller(context.User, visibilityService))
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

        // El browse y dashboard operativos de Pricing no se exponen a vendedores porque
        // no están filtrados por alcance comercial. Supervisores y jefes consultan el
        // reporte de solicitudes visible para ellos y desde ahí pueden abrir cada tarifa.
        if (string.Equals(path.TrimEnd('/'), "/api/pricing/rates", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/pricing/rates/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ratesIndex = Array.FindIndex(
            segments,
            value => string.Equals(value, "rates", StringComparison.OrdinalIgnoreCase)
        );

        if (ratesIndex < 0
            || ratesIndex + 1 >= segments.Length
            || !Guid.TryParse(segments[ratesIndex + 1], out var rateId))
        {
            await next(context);
            return;
        }

        if (!await visibilityService.CanViewRateAsync(context, rateId, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private static bool IsRestrictedSeller(
        ClaimsPrincipal user,
        SellerVisibilityService visibilityService)
    {
        var roleValues = user.Claims
            .Where(claim =>
                claim.Type == ClaimTypes.Role
                || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => Split(claim.Value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        var sellerRole = roleValues.Any(role =>
            role is "vendedor" or "seller" or "ventas"
            || role.Contains("vendedor")
            || role.Contains("seller"));

        var hasRequestCreate = visibilityService.HasScope(
            user,
            PricingConstants.Scopes.RateRequestCreate
        );
        var hasRateUpdate = visibilityService.HasScope(
            user,
            PricingConstants.Scopes.RateUpdate
        );

        return sellerRole || (hasRequestCreate && !hasRateUpdate);
    }

    private static IEnumerable<string> Split(string value)
        => value.Split(
            new[] { ' ', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
}
