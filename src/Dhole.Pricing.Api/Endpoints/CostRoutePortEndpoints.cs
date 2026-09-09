using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;

namespace Dhole.Pricing.Api.Endpoints;

public static class CostRoutePortEndpoints
{
    private const int MaximumPortsPerRole = 100;

    public static IEndpointRouteBuilder MapCostRoutePortEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/costs")
            .WithTags("Costs")
            .RequireAuthorization();

        group.MapGet("/{costId:guid}/route-ports", GetAsync)
            .RequireScope(PricingScopeNames.CostView);

        group.MapPut("/{costId:guid}/route-ports", ReplaceAsync)
            .RequireScope(PricingScopeNames.CostUpdate);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid costId,
        ICostRepository costs,
        ICostRoutePortSelectionStore routePorts,
        CancellationToken cancellationToken
    )
    {
        var cost = await costs.GetByIdWithIncotermsAsync(costId, cancellationToken);
        if (cost is null)
            return Results.NotFound();

        return Results.Ok(await routePorts.GetAsync(costId, cancellationToken));
    }

    private static async Task<IResult> ReplaceAsync(
        Guid costId,
        CostRoutePortSelectionRequest request,
        ICostRepository costs,
        ICostRoutePortSelectionStore routePorts,
        ICostCacheService cache,
        CancellationToken cancellationToken
    )
    {
        var cost = await costs.GetByIdWithIncotermsAsync(costId, cancellationToken);
        if (cost is null)
            return Results.NotFound();

        var polIds = Normalize(request.PolIds);
        var poeIds = Normalize(request.PoeIds);
        var podIds = Normalize(request.PodIds);

        if (
            polIds.Length > MaximumPortsPerRole
            || poeIds.Length > MaximumPortsPerRole
            || podIds.Length > MaximumPortsPerRole
        )
        {
            return Results.BadRequest(new
            {
                code = "Pricing.CostRoutePortsLimitExceeded",
                message = $"Cada rol permite un máximo de {MaximumPortsPerRole} puertos.",
            });
        }

        await routePorts.ReplaceAsync(costId, polIds, poeIds, podIds, cancellationToken);
        await cache.RemoveCostCacheAsync(costId, cancellationToken);
        await cache.RemoveCostsSelectAsync(cancellationToken);

        return Results.Ok(await routePorts.GetAsync(costId, cancellationToken));
    }

    private static Guid[] Normalize(IReadOnlyCollection<Guid>? ids) =>
        ids is null
            ? []
            : ids.Where(id => id != Guid.Empty).Distinct().ToArray();
}

public sealed record CostRoutePortSelectionRequest(
    IReadOnlyCollection<Guid>? PolIds,
    IReadOnlyCollection<Guid>? PoeIds,
    IReadOnlyCollection<Guid>? PodIds
);
