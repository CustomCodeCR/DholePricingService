using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Api.Endpoints;

public static class PricingConfigCatalogEndpoints
{
    private static readonly HashSet<string> AllowedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        PricingConstants.CatalogSlugs.Carriers,
        PricingConstants.CatalogSlugs.Pol,
        PricingConstants.CatalogSlugs.Poe,
        PricingConstants.CatalogSlugs.Pod,
        PricingConstants.CatalogSlugs.Currencies,
        PricingConstants.CatalogSlugs.Agents,
        PricingConstants.CatalogSlugs.ContainerTypes,
        PricingConstants.CatalogSlugs.PricingImportsProfiles,
        PricingConstants.CatalogSlugs.Incoterms,
    };

    public static IEndpointRouteBuilder MapPricingConfigCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/config")
            .WithTags("Pricing Config")
            .RequireAuthorization();

        group.MapGet("/catalogs/{groupSlug}", GetCatalogAsync);
        group.MapGet("/health", GetConfigHealthAsync);

        return app;
    }

    private static async Task<IResult> GetCatalogAsync(
        string groupSlug,
        IPricingConfigCatalogClient configCatalog,
        CancellationToken cancellationToken)
    {
        if (!AllowedGroups.Contains(groupSlug))
        {
            return Results.BadRequest(new
            {
                success = false,
                code = "Pricing.InvalidConfigCatalogGroup",
                message = $"El catálogo '{groupSlug}' no está habilitado para Pricing.",
                errors = Array.Empty<string>(),
            });
        }

        try
        {
            var items = await configCatalog.GetActiveByGroupAsync(groupSlug, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                data = items.Select(item => new
                {
                    id = item.Id,
                    catalogGroupSlug = item.CatalogGroupSlug,
                    code = item.Code,
                    slug = item.Slug,
                    name = item.Name,
                    value = item.Value,
                })
            });
        }
        catch (InvalidOperationException)
        {
            return Results.Json(new
            {
                success = false,
                code = PricingErrors.ConfigServiceUnavailable.Code,
                message = PricingErrors.ConfigServiceUnavailable.Message,
                errors = Array.Empty<string>(),
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetConfigHealthAsync(
        IPricingConfigCatalogClient configCatalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var currencies = await configCatalog.GetActiveByGroupAsync(
                PricingConstants.CatalogSlugs.Currencies,
                cancellationToken);

            return Results.Ok(new
            {
                service = "DholePricingService",
                dependency = "DholeConfigService",
                status = "Healthy",
                catalog = PricingConstants.CatalogSlugs.Currencies,
                activeItems = currencies.Count,
                timestamp = DateTimeOffset.UtcNow,
            });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Json(new
            {
                service = "DholePricingService",
                dependency = "DholeConfigService",
                status = "Unhealthy",
                message = exception.Message,
                timestamp = DateTimeOffset.UtcNow,
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
