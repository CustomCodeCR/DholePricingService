using System.Net;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestCompletionEndpoints
{
    private const string NotificationEventName = "notifications.notification.requested";

    public static IEndpointRouteBuilder MapRateRequestCompletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pricing/rate-requests/{requestId:guid}/complete-rate", CompleteAsync)
            .RequireIdempotency()
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
        IIntegrationEventOutboxWriter outbox,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await CompleteCoreAsync(requestId, request, db, dispatcher, outbox,
                httpContext, loggerFactory, requireLease: true, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Includes failed initial lookups and unexpected infrastructure errors.
            // Existing stage-specific errors remain 409/503; never send raw SQL,
            // server paths or credentials in the HTTP body.
            loggerFactory.CreateLogger("RateRequestCompletion").LogError(error,
                "Unhandled complete-rate failure. RequestId={RequestId}, RateId={RateId}, TraceId={TraceId}",
                requestId, request.RateId, httpContext.TraceIdentifier);
            return Results.Json(new
            {
                code = "Pricing.RateRequestCompletionUnexpected",
                message = "No se pudo completar la solicitud. La revisión puede estar guardada; verifique su estado antes de reintentar.",
                requestId,
                rateId = request.RateId,
                traceId = httpContext.TraceIdentifier,
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // The approval endpoint can finish an already-linked request without a user lease.
    // Never infer an association: only requests explicitly linked during rate creation qualify.
    internal static async Task TryCompleteApprovedRateAsync(
        Guid rateId,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        IIntegrationEventOutboxWriter outbox,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Reconcile both older open requests and completed requests whose email
        // is still waiting for a margin decision or a delivery retry.
        // An unprepared modification must never send the original quote again.
        var candidates = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId
                && (x.Status == RateRequestStatus.Open
                    || (x.Status == RateRequestStatus.Completed && x.DeliveryConfirmedAtUtc == null)))
            .Select(x => new { x.Id, x.PayloadJson, x.Status, x.PreparedRevisionNumber })
            .ToListAsync(cancellationToken);

        foreach (var requestId in candidates
            .Where(x => !RateModificationRequestPayload.IsModification(x.PayloadJson)
                || (x.Status == RateRequestStatus.Completed && x.PreparedRevisionNumber.HasValue))
            .Select(x => x.Id))
        {
            try
            {
                var result = await CompleteCoreAsync(
                    requestId, new CompleteRateRequest(rateId), db, dispatcher, outbox,
                    httpContext, loggerFactory, requireLease: false, cancellationToken);
                if (result is IStatusCodeHttpResult status && status.StatusCode >= 400)
                {
                    loggerFactory.CreateLogger("RateRequestCompletion").LogWarning(
                        "La tarifa {RateId} fue aprobada, pero la solicitud {RequestId} sigue pendiente. HTTP {Status}.",
                        rateId, requestId, status.StatusCode);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Approval is already committed; never report it as failed due to a PDF/email issue.
                loggerFactory.CreateLogger("RateRequestCompletion").LogError(
                    error, "No se pudo finalizar la solicitud {RequestId} de la tarifa aprobada {RateId}.",
                    requestId, rateId);
            }
        }
    }

    private static async Task<IResult> CompleteCoreAsync(
        Guid requestId,
        CompleteRateRequest request,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        IIntegrationEventOutboxWriter outbox,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        bool requireLease,
        CancellationToken cancellationToken
    )
    {
        if (request.RateId == Guid.Empty)
            return Results.BadRequest(new { message = "La tarifa es requerida." });

        var entity = await db.RateRequests
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (entity is null)
            return Results.NotFound();

        var alreadyCompleted = entity.Status == RateRequestStatus.Completed
            && entity.RateId == request.RateId;
        if (alreadyCompleted && entity.DeliveryConfirmedAtUtc.HasValue)
            return Results.NoContent();
        if (entity.Status != RateRequestStatus.Open && !alreadyCompleted)
            return Results.Conflict(new { message = "No se puede enviar una solicitud cancelada o vinculada a otra tarifa." });

        if (requireLease && !alreadyCompleted && !RateRequestLeaseEndpoints.HasActiveLease(httpContext, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        // Automated completion may only touch a request already explicitly linked to this rate.
        if (!requireLease && entity.RateId != request.RateId)
            return Results.Conflict(new { message = "La solicitud no está vinculada a esta tarifa." });

        var isModification = RateModificationRequestPayload.IsModification(entity.PayloadJson);
        // Being linked to an existing quote or releasing a tab lease is not
        // approval to deliver a seller-requested amendment. Require an explicit,
        // user-initiated intent from the Send revision action.
        if (isModification && !alreadyCompleted
            && (request.Confirmation != "SendSellerModification" || !requireLease))
            return Results.Conflict(new {
                code = "Pricing.RateModificationExplicitDeliveryRequired",
                message = "La modificación sólo puede completarse con Enviar revisión al vendedor."
            });
        if (isModification && entity.RateId != request.RateId)
            return Results.Conflict(new { message = "Una modificación debe conservar su tarifa original." });

        if (string.IsNullOrWhiteSpace(entity.SellerEmail))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.RateRequestSellerEmailRequired",
                message = "La solicitud no tiene correo del vendedor. Corrija el usuario solicitante antes de completar la tarifa.",
            });
        }

        var rate = await db.RateHeaders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.RateId && !x.IsDeleted, cancellationToken);

        if (rate is null)
            return Results.BadRequest(new { message = "La tarifa indicada no existe." });

        if (isModification
            && (rate.RevisionNumber <= RateModificationRequestPayload.SourceRevisionNumber(entity.PayloadJson)
                || entity.PreparedRevisionNumber != rate.RevisionNumber))
            return Results.Conflict(new {
                code = "Pricing.RateModificationRevisionRequired",
                message = "Primero guarde la modificación desde el wizard y prepare esta revisión antes de enviarla al vendedor."
            });

        if (entity.RateId.HasValue && entity.RateId != request.RateId)
            return Results.Conflict(new { message = "La solicitud ya está vinculada a otra tarifa." });

        // Completing Pricing's work is independent of margin authorization,
        // PDF generation and SMTP availability. Store this transition first.
        if (!alreadyCompleted)
        {
            try
            {
                entity.AttachRate(request.RateId);
                if (isModification)
                    entity.MarkSellerModificationReadyForDelivery(DateTime.UtcNow);
                else
                    entity.MarkCompleted(DateTime.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException error) when (!cancellationToken.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("RateRequestCompletion").LogError(
                    error,
                    "Complete-rate rejected an invalid request state. RequestId={RequestId}, RateId={RateId}, " +
                    "Status={Status}, PreparedRevisionNumber={PreparedRevision}, ActualRevision={ActualRevision}, TraceId={TraceId}",
                    requestId, request.RateId, entity.Status, entity.PreparedRevisionNumber,
                    rate.RevisionNumber, httpContext.TraceIdentifier);
                return Results.Conflict(new
                {
                    code = "Pricing.RateRequestCompletionInvalidState",
                    message = "No se pudo completar esta solicitud porque su estado no permite el cierre. La QUO existente no debe duplicarse.",
                    requestId,
                    rateId = request.RateId,
                });
            }
            catch (DbUpdateException error) when (!cancellationToken.IsCancellationRequested)
            {
                // A 503 is not sufficient to diagnose a persistent PostgreSQL
                // constraint violation. Expose only safe, stable identifiers;
                // never return SQL text, payload_json or database credentials.
                var postgres = error.GetBaseException() as Npgsql.PostgresException;
                var sqlState = postgres?.SqlState;
                var constraintName = postgres?.ConstraintName;
                var tableName = postgres?.TableName;
                loggerFactory.CreateLogger("RateRequestCompletion").LogError(
                    error,
                    "Complete-rate persistence failed. RequestId={RequestId}, RateId={RateId}, " +
                    "Status={Status}, PreparedRevisionNumber={PreparedRevision}, ActualRevision={ActualRevision}, " +
                    "SqlState={SqlState}, ConstraintName={ConstraintName}, TableName={TableName}, TraceId={TraceId}",
                    requestId, request.RateId, entity.Status, entity.PreparedRevisionNumber,
                    rate.RevisionNumber, sqlState, constraintName, tableName, httpContext.TraceIdentifier);
                return Results.Json(new
                {
                    code = "Pricing.RateRequestCompletionPersistenceFailed",
                    message = "La revisión de la QUO puede estar guardada, pero PostgreSQL rechazó el cierre de la solicitud. No cree otra revisión.",
                    requestId,
                    rateId = request.RateId,
                    traceId = httpContext.TraceIdentifier,
                    diagnostic = new
                    {
                        sqlState,
                        constraintName,
                        tableName,
                        // A null value indicates a non-Postgres update failure;
                        // the original exception is still in structured server logs.
                    },
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        // Never bypass an approval. The approval hook and background reconciler
        // will issue the PDF/email when the quote becomes commercially releasable.
        if (rate.Status == RateStatus.PendingApproval
            || (rate.RequiredApproval && rate.TotalSaleAmount > 0m))
            return Results.Accepted(value: new { status = "CompletedAwaitingMarginApproval", rateId = request.RateId });

        if (rate.Status is RateStatus.RejectedByManagement or RateStatus.RejectedByClient
            or RateStatus.Cancelled or RateStatus.Closed)
            return Results.Accepted(value: new { status = "CompletedDeliveryOnHold", rateId = request.RateId });

        // The revision and work-item completion have already been committed.
        // PDF generation, commercial status and outbox delivery are recoverable
        // operations. Never turn those failures into a misleading "save failed" 500.
        try
        {
            var documentResult = await dispatcher.DispatchAsync(
                new GenerateRateDocumentCommand(request.RateId, null, "pdf"),
                cancellationToken
            );
    
            if (!documentResult.IsSuccess)
                return Results.Accepted(value: new { status = "CompletedDeliveryRetryPending", rateId = request.RateId });
    
            if (rate.Status != RateStatus.Sent)
            {
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
                    return Results.Accepted(value: new { status = "CompletedDeliveryRetryPending", rateId = request.RateId });
            }
    
            var document = documentResult.Value;
            var sellerName = WebUtility.HtmlEncode(entity.SellerName ?? entity.ExecutiveName ?? "Vendedor");
            var clientName = WebUtility.HtmlEncode(entity.ClientName ?? "Cliente");
            var routeText = BuildRoute(entity);
            var route = WebUtility.HtmlEncode(routeText);
            var quoteNumberText = rate.QuoNumber ?? rate.RateCode;
            var quoteNumber = WebUtility.HtmlEncode(quoteNumberText);
            var sellerEmail = entity.SellerEmail.Trim();
            var sellerDisplayName = entity.SellerName ?? entity.ExecutiveName;
            var emailBody = $"""
                <p>Hola {sellerName},</p>
                <p>Pricing { (isModification ? "actualizó" : "terminó") } la tarifa solicitada para <strong>{clientName}</strong>.</p>
                <p><strong>Tarifa:</strong> {quoteNumber}<br />
                <strong>Ruta:</strong> {route}</p>
                <p>Se adjunta la cotización completa en PDF para que pueda enviarla al cliente.</p>
                <p>Grupo Castro Fallas - Pricing</p>
                """;
    
            // El envío de correo queda en Notifications para que una caída de SMTP no convierta
            // la creación de tarifa en un 502. Notifications maneja reintentos y adjunta el PDF.
            await outbox.WriteAsync(
                NotificationEventName,
                NotificationEventName,
                new
                {
                    notificationType = "pricing.requested-rate.completed.email",
                    channel = "Email",
                    entityType = "RateRequest",
                    entityId = requestId.ToString(),
                    subject = $"{(isModification ? "Tarifa Actualizada" : "Tarifa Lista")} - {(string.IsNullOrWhiteSpace(rate.RateName) ? quoteNumberText : rate.RateName.Trim())}",
                    body = emailBody,
                    payload = new
                    {
                        rateRequestId = requestId,
                        rateId = request.RateId,
                        quo = quoteNumberText,
                        client = entity.ClientName,
                        route = routeText,
                        cc = new[]
                        {
                            "jmesen@castrofallas.com",
                            "pricingcf@castrofallas.com",
                            "cquiros@castrofallas.com",
                        },
                        attachments = new[]
                        {
                            new
                            {
                                fileName = document.FileName,
                                contentType = document.ContentType,
                                contentBase64 = Convert.ToBase64String(document.Content),
                            },
                        },
                    },
                    recipients = new[]
                    {
                        new
                        {
                            userId = entity.SellerUserId?.ToString(),
                            address = sellerEmail,
                            displayName = sellerDisplayName,
                        },
                    },
                },
                $"{requestId}:seller-email",
                cancellationToken
            );
    
            // Mantener además la notificación interna/SignalR para el vendedor.
            await outbox.WriteAsync(
                NotificationEventName,
                NotificationEventName,
                new
                {
                    notificationType = "pricing.requested-rate.completed",
                    channel = "System",
                    entityType = "RateRequest",
                    entityId = requestId.ToString(),
                    subject = isModification ? "Modificación de tarifa completada" : "Tarifa solicitada lista",
                    body = $"Pricing {(isModification ? "actualizó" : "terminó")} la tarifa {quoteNumberText} para {entity.ClientName ?? "el cliente"}.",
                    payload = new
                    {
                        rateRequestId = requestId,
                        rateId = request.RateId,
                        quo = quoteNumberText,
                        client = entity.ClientName,
                        route = routeText,
                        pdfFileName = document.FileName,
                    },
                    recipients = new[]
                    {
                        new
                        {
                            userId = entity.SellerUserId?.ToString(),
                            address = sellerEmail,
                            displayName = sellerDisplayName,
                        },
                    },
                },
                $"{requestId}:system",
                cancellationToken
            );
    
            entity.MarkDeliveryQueued(DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
    
    
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("RateRequestCompletion").LogError(
                error,
                "Tarifa {RateId} ya guardada para solicitud {RequestId}; fallo en PDF, estado o outbox. " +
                "Entrega pendiente de reintento. TraceId={TraceId}",
                request.RateId, requestId, httpContext.TraceIdentifier);
            // SellerRateDeliveryRetryWorker processes Completed requests whose
            // DeliveryConfirmedAtUtc is still null. Keep the same request and revision:
            // retrying here must NOT create a second quotation or new revision.
            return Results.Accepted(value: new
            {
                status = "CompletedDeliveryRetryPending",
                code = "Pricing.RateRequestDeliveryDeferred",
                rateId = request.RateId,
                requestId,
            });
        }

        return Results.NoContent();
    }

    private static string BuildRoute(Dhole.Pricing.Domain.Rates.Entities.RateRequest request)
    {
        var parts = new[] { request.OriginName, request.PoeName, request.PodName, request.DestinationName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? "Ruta sin definir" : string.Join(" → ", parts);
    }

    private sealed record CompleteRateRequest(Guid RateId, string? Confirmation = null);
}
