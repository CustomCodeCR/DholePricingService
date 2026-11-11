using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
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
        AcceptedRateOpeningsNotificationService openingsNotificationService,
        SellerRateAccessService sellerAccess,
        ILoggerFactory loggerFactory,
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

        // Reintentos después de guardar correctamente deben ser idempotentes.
        // QueueAsync utiliza una clave estable para no duplicar correos de Apertura.
        if (currentRate.Status == status)
        {
            if (status == RateStatus.AcceptedByClient)
            {
                // A duplicate PATCH must not send the unpublished newer revision
                // to Apertura after a prior successful direct Open decision.
                var unpublishedNewer = await db.Set<RateRevision>().AsNoTracking()
                    .AnyAsync(x => x.RateHeaderId == rateId
                        && x.RevisionNumber < currentRate.RevisionNumber
                        && (x.Status == "Sent" || x.Status == "RequestedByClient" || x.Status == "Expired"),
                        cancellationToken);
                if (unpublishedNewer)
                    return Results.Ok(new {
                        status = status.ToString(),
                        openingNotificationDeferred = true,
                    });

                await openingsNotificationService.QueueAsync(rateId, RateAcceptanceActor.Seller, cancellationToken);
            }
            return Results.NoContent();
        }

        // The seller can decide on a PREVIOUSLY ISSUED quote even if Pricing
        // reopened the current header. Do not grant this to a brand-new unsent
        // draft, low-margin pending approval, or active seller modification.
        var isOpenDecision = currentRate.Status == RateStatus.Open;
        var decisionOnOlderPublishedRevision = false;
        if (isOpenDecision)
        {
            if (currentRate.RequiredApproval)
                return Results.Conflict(new {
                    code = "Pricing.SellerDecisionRequiresApproval",
                    message = "La tarifa requiere aprobación antes de registrar una decisión comercial.",
                });

            var published = await db.Set<RateRevision>().AsNoTracking()
                .Where(x => x.RateHeaderId == rateId
                    && (x.Status == "Sent" || x.Status == "RequestedByClient" || x.Status == "Expired"))
                .OrderByDescending(x => x.RevisionNumber)
                .Select(x => new { x.RevisionNumber, x.Status })
                .FirstOrDefaultAsync(cancellationToken);
            if (published is null || published.RevisionNumber > currentRate.RevisionNumber)
                return Results.Conflict(new {
                    code = "Pricing.SellerDecisionRequiresPublishedRevision",
                    message = "La tarifa Abierta necesita una revisión previamente enviada para aceptarse o rechazarse.",
                });

            var openWork = await db.RateRequests.AsNoTracking()
                .Where(x => x.RateId == rateId && x.Status == RateRequestStatus.Open)
                .Select(x => x.PayloadJson)
                .ToListAsync(cancellationToken);
            if (openWork.Any(RateModificationRequestPayload.IsModification))
                return Results.Conflict(new {
                    code = "Pricing.SellerModificationStillOpen",
                    message = "Pricing todavía está procesando la modificación. Espere a que termine antes de registrar la decisión.",
                });

            decisionOnOlderPublishedRevision = currentRate.RevisionNumber > published.RevisionNumber;
        }
        else if (currentRate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient or RateStatus.Expired))
        {
            return Results.Conflict(new
            {
                code = "Pricing.SellerRateNotAwaitingClient",
                message = "La tarifa solo puede aceptarse o rechazarse cuando está enviada, vencida o Abierta con una revisión previamente publicada.",
            });
        }

        if (status == RateStatus.AcceptedByClient && request.Observations?.Trim().Length > 2000)
            return Results.BadRequest(new {
                code = "Pricing.SellerAcceptanceObservationsTooLong",
                message = "Las observaciones de apertura no pueden superar 2000 caracteres.",
            });

        var acceptanceObservations = status == RateStatus.AcceptedByClient
            ? request.Observations?.Trim() : null;

        if (status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerRejectionReasonRequired",
                message = "Indique el motivo por el que el cliente rechazó la tarifa.",
            });
        }

        // Único flujo de dominio para decisiones con IDTRA y sin IDTRA.
        // SetCommercialStatus registra AcceptedAtUtc, revisión, auditoría y eventos.
        // La capacidad del consolidado no es un requisito de aceptación comercial.
        try
        {
            var result = await dispatcher.DispatchAsync(
                new SetRateStatusCommand(
                    rateId,
                    status,
                    request.Reason,
                    status == RateStatus.AcceptedByClient ? request.IdtraNumber : null,
                    currentUserId,
                    AllowDirectClientDecision: isOpenDecision,
                    AcceptanceObservations: acceptanceObservations
                ),
                cancellationToken
            );

            if (result.IsSuccess && status == RateStatus.AcceptedByClient)
            {
                if (decisionOnOlderPublishedRevision)
                {
                    // The opening-email generator reads the live header and would
                    // attach an UNPUBLISHED PDF. Never send that draft to Apertura.
                    // Keep the decision committed; Pricing must publish/reconcile
                    // the accepted revision before an opening PDF can be sent.
                    loggerFactory.CreateLogger(nameof(SellerRateStatusEndpoints)).LogWarning(
                        "Seller accepted a previously published revision with an unpublished newer draft. " +
                        "Opening document deferred. RateId={RateId} PublishedDecisionStatus={Status}",
                        rateId, status);
                }
                else
                {
                    await openingsNotificationService.QueueAsync(
                        rateId,
                        RateAcceptanceActor.Seller,
                        cancellationToken,
                        acceptanceObservations
                    );
                }
            }

            if (result.IsSuccess && decisionOnOlderPublishedRevision)
                return Results.Ok(new {
                    status = status.ToString(),
                    openingNotificationDeferred = status == RateStatus.AcceptedByClient,
                    message = "Decisión guardada sobre la última revisión publicada. " +
                              "El envío del PDF a Apertura queda pendiente de validación por Pricing.",
                });

            return EndpointResults.FromResult(result, httpContext);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger(nameof(SellerRateStatusEndpoints)).LogError(
                exception,
                "Error al registrar decisión de cliente. RateId={RateId} Status={Status} TraceId={TraceId}",
                rateId,
                status,
                httpContext.TraceIdentifier
            );
            return Results.Problem(
                title: "No se pudo registrar la decisión del cliente.",
                detail: "No se confirmó la operación. Revise el estado de la tarifa antes de reintentar.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "Pricing.SellerRateStatusFailed",
                    ["traceId"] = httpContext.TraceIdentifier,
                }
            );
        }
    }

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber,
        string? Observations = null
    );
}
