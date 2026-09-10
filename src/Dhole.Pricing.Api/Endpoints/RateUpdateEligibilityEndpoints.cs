using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
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

    private static async Task<IResult> GetAsync(
        Guid rateId,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var rateStatus = await db.RateHeaders
            .AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => (RateStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (!rateStatus.HasValue)
            return Results.NotFound();

        var linkedRequests = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.RateId == rateId)
            .Select(x => new { x.Id, x.Status })
            .ToListAsync(cancellationToken);

        var decision = RateUpdateWindowPolicy.Evaluate(
            rateStatus.Value,
            linkedRequests.Count > 0,
            linkedRequests.Any(x => x.Status == RateRequestStatus.Open)
        );

        return Results.Ok(new
        {
            canUpdate = decision.CanUpdate,
            requiresReason = true,
            rateStatus = rateStatus.Value.ToString(),
            updateWindow = decision.UpdateWindow,
            message = decision.Message,
            requestIds = linkedRequests.Select(x => x.Id).ToArray(),
        });
    }
}
