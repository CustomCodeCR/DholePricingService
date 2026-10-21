using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateStatusEndpoints
{
    public static IEndpointRouteBuilder MapSellerRateStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/pricing/seller-rates/{rateId:guid}/received", MarkReceivedAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapPatch("/api/pricing/seller-rates/{rateId:guid}/status", SetStatusAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        return app;
    }

    private static async Task<IResult> MarkReceivedAsync(
        Guid rateId,
        ServiceDbContext db,
        SellerRateAccessService sellerAccess,
        IPricingAuditService audit,
        IRateHeaderCacheService cache,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var sellerId = httpContext.GetCurrentUserId();
        if (!sellerId.HasValue || sellerId.Value == Guid.Empty) return Results.Unauthorized();

        var rate = await db.RateHeaders
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        // Only the explicitly assigned executive can acknowledge; never "Castro Fallas".
        if (!await sellerAccess.IsAssignedExecutiveAsync(rate, sellerId.Value, cancellationToken))
            return Results.Forbid();
        if (rate.SellerReceivedAtUtc.HasValue) return Results.NoContent();

        if (rate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient))
            return Results.Conflict(new {
                code = "Pricing.SellerReceiptRequiresSent",
                message = "Solo puede marcarse Recibida una tarifa enviada al vendedor.",
            });

        var before = PricingAuditSnapshots.From(rate);
        rate.MarkReceivedBySeller(sellerId.Value);
        await audit.PublishAsync(
            new PricingAuditEvent(
                EventType: PricingAuditEventTypes.RateHeaderUpdated,
                Action: PricingAuditActions.Updated,
                EntityType: PricingAuditEntityTypes.RateHeader,
                EntityId: rate.Id,
                ActorUserId: sellerId,
                Before: before,
                After: PricingAuditSnapshots.From(rate),
                Payload: new {
                    rate.Id,
                    Source = "SellerReceiptAcknowledgement",
                    rate.SellerReceivedAtUtc,
                    rate.SellerReceivedBy,
                }
            ),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SetStatusAsync(
        Guid rateId,
        SellerRateStatusRequest request,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        IRateHeaderRepository rateHeaders,
        IPricingAuditService audit,
        IRateHeaderCacheService cache,
        AcceptedRateOpeningsNotificationService openingsNotificationService,
        SellerRateAccessService sellerAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        if (!Enum.TryParse<RateStatus>(request.Status, true, out var status)
            || status is not (RateStatus.AcceptedByClient or RateStatus.RejectedByClient))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerInvalidRateStatus",
                message = "El vendedor únicamente puede marcar su tarifa como aceptada o rechazada.",
            });
        }

        var rateRequest = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.SellerUserId == currentUserId.Value && x.RateId == rateId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var currentRate = await db.RateHeaders
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

        if (currentRate is null)
            return Results.NotFound();

        if (rateRequest is null
            && !await sellerAccess.IsAssignedExecutiveAsync(currentRate, currentUserId.Value, cancellationToken))
            return Results.Forbid();

        if (currentRate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient))
        {
            return Results.Conflict(new
            {
                code = "Pricing.SellerRateNotAwaitingClient",
                message = "La tarifa solo puede aceptarse o rechazarse cuando está enviada al cliente.",
            });
        }

        if (status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerRejectionReasonRequired",
                message = "Indique el motivo por el que el cliente rechazó la tarifa.",
            });
        }

        if (AcceptedRateOpeningsNotificationService.HasPendingIdtra(
            status, currentRate.IdtraNumber, request.IdtraNumber))
        {
            var capacity = await rateHeaders.GetOwnLclCapacityForRateAsync(
                currentRate.Id,
                cancellationToken
            );
            if (capacity is not null
                && capacity.RequestedCbm > capacity.RemainingCbm + 0.000001m)
            {
                return Results.Conflict(new
                {
                    code = "Pricing.OwnLclCapacityExceeded",
                    message = $"El consolidado #{capacity.ConsolidationNumber} solo tiene {capacity.RemainingCbm:0.###} CBM disponibles para aprobación y esta tarifa requiere {capacity.RequestedCbm:0.###} CBM.",
                });
            }

            var before = PricingAuditSnapshots.From(currentRate);

            db.Entry(currentRate).Property(x => x.Status).CurrentValue = RateStatus.AcceptedByClient;
            db.Entry(currentRate).Property(x => x.RequiredApproval).CurrentValue = false;

            await audit.PublishAsync(
                new PricingAuditEvent(
                    EventType: PricingAuditEventTypes.RateHeaderUpdated,
                    Action: PricingAuditActions.Updated,
                    EntityType: PricingAuditEntityTypes.RateHeader,
                    EntityId: currentRate.Id,
                    ActorUserId: currentUserId,
                    Before: before,
                    After: PricingAuditSnapshots.From(currentRate),
                    Payload: new
                    {
                        currentRate.Id,
                        Status = currentRate.Status.ToString(),
                        currentRate.IdtraNumber,
                        Source = "SellerClientDecision",
                        IdtraPending = true,
                    }
                ),
                cancellationToken
            );

            await db.SaveChangesAsync(cancellationToken);
            await cache.RemoveRateHeaderCacheAsync(currentRate.Id, cancellationToken);
            await openingsNotificationService.QueueAsync(currentRate.Id, RateAcceptanceActor.Seller, cancellationToken);
            return Results.NoContent();
        }

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                status == RateStatus.AcceptedByClient ? request.IdtraNumber : null,
                currentUserId
            ),
            cancellationToken
        );

        if (result.IsSuccess && status == RateStatus.AcceptedByClient)
            await openingsNotificationService.QueueAsync(rateId, RateAcceptanceActor.Seller, cancellationToken);

        return EndpointResults.FromResult(result, httpContext);
    }

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber
    );
}
