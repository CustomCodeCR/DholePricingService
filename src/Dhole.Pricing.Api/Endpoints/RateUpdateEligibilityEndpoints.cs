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

    private static async Task<IResult> GetAsync(
        Guid rateId,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var rate = await db.RateHeaders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();

        var decision = Dhole.Pricing.Api.Services.RateUpdateWindowPolicy.Evaluate(
            rate.Status, hasLinkedRequest: false, requestStillOpen: false);
        var expired = Dhole.Pricing.Api.Services.ExpiredRateUpdateApprovalPolicy.RequiresApproval(
            rate, DateTimeOffset.UtcNow);
        var approved = expired && await Dhole.Pricing.Api.Services.ExpiredRateUpdateApprovalPolicy.IsApprovedAsync(
            db, rate.Id, rate.RevisionNumber, cancellationToken);
        var canUpdate = decision.CanUpdate && (!expired || approved);
        return Results.Ok(new
        {
            canUpdate,
            requiresReason = canUpdate,
            requiresApproval = decision.CanUpdate && expired && !approved,
            rateStatus = rate.Status.ToString(),
            updateWindow = expired ? "ExpiredManagerApproval" : decision.UpdateWindow,
            message = expired && !approved
                ? "Tarifa vencida: indique por qué se dejó vencer y solicite autorización a Roy o José Pablo."
                : decision.Message,
            requestIds = Array.Empty<Guid>(),
        });
    }
}
