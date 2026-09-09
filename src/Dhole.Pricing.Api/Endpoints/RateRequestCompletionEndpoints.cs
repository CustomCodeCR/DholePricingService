using System.Net;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
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
        PricingEmailService emailService,
        ILoggerFactory loggerFactory,
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

        var documentResult = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(request.RateId, null, "pdf"),
            cancellationToken
        );

        if (!documentResult.IsSuccess)
            return EndpointResults.FromResult(documentResult, httpContext);

        // Un reintento de correo puede encontrar la tarifa ya en Sent aunque la solicitud siga
        // abierta. No repetimos la transición; únicamente reintentamos documento/correo.
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
                return EndpointResults.FromResult(statusResult, httpContext);
        }

        var document = documentResult.Value;
        var sellerName = WebUtility.HtmlEncode(entity.SellerName ?? entity.ExecutiveName ?? "Vendedor");
        var clientName = WebUtility.HtmlEncode(entity.ClientName ?? "Cliente");
        var routeText = BuildRoute(entity);
        var route = WebUtility.HtmlEncode(routeText);
        var quoteNumberText = rate.QuoNumber ?? rate.RateCode;
        var quoteNumber = WebUtility.HtmlEncode(quoteNumberText);

        try
        {
            await emailService.SendAsync(
                entity.SellerEmail.Trim(),
                $"Tarifa lista - {entity.ClientName ?? rate.QuoNumber ?? rate.RateCode}",
                $"""
                <p>Hola {sellerName},</p>
                <p>Pricing terminó la tarifa solicitada para <strong>{clientName}</strong>.</p>
                <p><strong>Tarifa:</strong> {quoteNumber}<br />
                <strong>Ruta:</strong> {route}</p>
                <p>Se adjunta la cotización completa en PDF para que pueda enviarla al cliente.</p>
                <p>Grupo Castro Fallas - Pricing</p>
                """,
                new PricingEmailAttachment(document.FileName, document.ContentType, document.Content),
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("RateRequestCompletionEmail").LogError(
                exception,
                "No se pudo enviar al vendedor {SellerEmail} la tarifa {RateId} de la solicitud {RateRequestId}.",
                entity.SellerEmail,
                request.RateId,
                requestId
            );

            return Results.Problem(
                title: "No se pudo enviar la tarifa al vendedor",
                detail: "La tarifa quedó preparada, pero el correo con el PDF no pudo enviarse. La solicitud permanece abierta para reintentar.",
                statusCode: StatusCodes.Status502BadGateway
            );
        }

        entity.AttachRate(request.RateId);
        entity.MarkCompleted(DateTime.UtcNow);

        // Notifications persiste el mensaje de sistema y lo retransmite por SignalR al grupo
        // del usuario vendedor. Se encola junto con la finalización para no perder la alerta.
        await outbox.WriteAsync(
            NotificationEventName,
            NotificationEventName,
            new
            {
                notificationType = "pricing.requested-rate.completed",
                channel = "System",
                entityType = "RateRequest",
                entityId = requestId.ToString(),
                subject = "Tarifa solicitada lista",
                body = $"Pricing terminó la tarifa {quoteNumberText} para {entity.ClientName ?? "el cliente"}.",
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
                        userId = entity.SellerUserId.ToString(),
                        address = entity.SellerEmail.Trim(),
                        displayName = entity.SellerName ?? entity.ExecutiveName,
                    },
                },
            },
            requestId.ToString(),
            cancellationToken
        );

        await db.SaveChangesAsync(cancellationToken);

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

    private sealed record CompleteRateRequest(Guid RateId);
}
