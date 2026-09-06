using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
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

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
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

        var orderedIds = linkedIds.Distinct().ToArray();
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

        return Results.Ok(items);
    }
}
