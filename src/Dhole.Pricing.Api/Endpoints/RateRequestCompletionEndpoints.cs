using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestCompletionEndpoints
{
    public static IEndpointRouteBuilder MapRateRequestCompletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/rate-requests/{requestId:guid}/complete-rate", CompleteAsync)
            .WithTags("Rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        return app;
    }

    private static async Task<IResult> CompleteAsync(
        Guid requestId,
        CompleteRateRequest request,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.RateId == Guid.Empty)
            return Results.BadRequest(new { message = "La tarifa es requerida." });

        var entity = await db.RateRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (entity is null)
            return Results.NotFound();

        if (entity.Status != RateRequestStatus.Open)
        {
            if (entity.RateId == request.RateId)
                return Results.NoContent();

            return Results.Conflict(new { message = "La solicitud ya fue completada." });
        }

        var rate = await db.RateHeaders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.RateId && !x.IsDeleted, cancellationToken);

        if (rate is null)
            return Results.BadRequest(new { message = "La tarifa indicada no existe." });

        var actor = httpContext.GetCurrentUserId();
        var statusResult = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                request.RateId,
                RateStatus.Sent,
                null,
                null,
                actor
            ),
            cancellationToken
        );

        if (!statusResult.IsSuccess)
            return EndpointResults.FromResult(statusResult, httpContext);

        entity.AttachRate(request.RateId);
        entity.MarkCompleted(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private sealed record CompleteRateRequest(Guid RateId);
}
