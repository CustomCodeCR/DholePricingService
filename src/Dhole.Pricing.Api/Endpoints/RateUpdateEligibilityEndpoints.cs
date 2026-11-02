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

        // Keep the eligibility advertised to the wizard consistent with PUT
        // /api/pricing/rates/{id}. Closed quotes are immutable; other states
        // can create an auditable revision.
        var decision = Dhole.Pricing.Api.Services.RateUpdateWindowPolicy.Evaluate(
            rateStatus.Value, hasLinkedRequest: false, requestStillOpen: false);
        return Results.Ok(new
        {
            canUpdate = decision.CanUpdate,
            requiresReason = decision.CanUpdate,
            rateStatus = rateStatus.Value.ToString(),
            updateWindow = decision.UpdateWindow,
            message = decision.Message,
            requestIds = Array.Empty<Guid>(),
        });
    }
}
