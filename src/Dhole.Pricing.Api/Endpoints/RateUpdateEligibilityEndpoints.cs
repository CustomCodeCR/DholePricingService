using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateUpdateEligibilityEndpoints
{
    public static IEndpointRouteBuilder MapRateUpdateEligibilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/rates/{rateId:guid}/update-eligibility", GetAsync)
            .WithTags("Rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateView);
        return app;
    }

    private static async Task<IResult> GetAsync(Guid rateId, ServiceDbContext db, CancellationToken cancellationToken)
    {
        var rateStatus = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => (RateStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (!rateStatus.HasValue) return Results.NotFound();

        // Compatibility endpoint for older DholeWeb builds. Updating a rate no longer has
        // an artificial window or mandatory reason just because it is an update.
        return Results.Ok(new
        {
            canUpdate = true,
            requiresReason = false,
            rateStatus = rateStatus.Value.ToString(),
            updateWindow = (string?)null,
            message = "La tarifa puede actualizarse con las mismas reglas funcionales de una tarifa nueva.",
            requestIds = Array.Empty<Guid>(),
        });
    }
}
