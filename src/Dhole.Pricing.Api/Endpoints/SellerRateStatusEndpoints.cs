using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
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

        app.MapGet("/api/pricing/seller-rates/{rateId:guid}/options", GetGroupOptionsAsync)
            .WithTags("Seller rates").RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapPost("/api/pricing/seller-rates/{rateId:guid}/group-select", SelectSellerGroupOptionAsync)
            .WithTags("Seller rates").RequireAuthorization()
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


    private static async Task<IResult> GetGroupOptionsAsync(
        Guid rateId, ServiceDbContext db, SellerRateAccessService sellerAccess,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var requested = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (requested is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(requested, userId.Value, cancellationToken))
            return Results.Forbid();

        var connection = db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(cancellationToken);
        var options = new List<(Guid Id, string BaseQuote, string Suffix,
            string Selection, string Status, string Poe, string? Pod, string Carrier)>();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.rate_id, a.base_quote, a.suffix, a.status,
                       h.status, h.poe_name, h.pod_name, h.carrier_name
                FROM pricing.rate_quote_alternatives a
                JOIN pricing."RateHeaders" h ON h.id = a.rate_id AND NOT h.is_deleted
                WHERE a.parent_rate_id = (
                    SELECT parent_rate_id FROM pricing.rate_quote_alternatives
                    WHERE rate_id = @rate_id LIMIT 1)
                ORDER BY length(a.suffix), a.suffix;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@rate_id";
            parameter.Value = rateId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                options.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7)));
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }

        if (options.Count == 0)
            return Results.Ok(new { parentQuoteNumber = requested.QuoNumber ?? requested.RateCode,
                selectedRateId = (Guid?)null, alternatives = Array.Empty<object>() });

        var available = new List<object>();
        foreach (var option in options)
        {
            var member = await db.RateHeaders.AsNoTracking()
                .FirstOrDefaultAsync(rate => rate.Id == option.Id && !rate.IsDeleted, cancellationToken);
            if (member is null || !await sellerAccess.CanReadAsync(member, userId.Value, cancellationToken))
                continue;
            // Never expose prices or unpublished supplier costs from a draft to Sales.
            available.Add(new
            {
                rateId = option.Id, suffix = option.Suffix,
                status = option.Selection, rateStatus = option.Status,
                poeName = option.Poe, podName = option.Pod, carrierName = option.Carrier
            });
        }
        return Results.Ok(new
        {
            parentQuoteNumber = options[0].BaseQuote,
            selectedRateId = options.Where(x => x.Selection == "Selected")
                .Select(x => (Guid?)x.Id).FirstOrDefault(),
            alternatives = available,
        });
    }

    private static async Task<IResult> SelectSellerGroupOptionAsync(
        Guid rateId, ServiceDbContext db, SellerRateAccessService sellerAccess,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, userId.Value, cancellationToken))
            return Results.Forbid();
        if (rate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient or RateStatus.Expired))
            return Results.Conflict(new { code = "Pricing.SellerAlternativeNotSent",
                message = "Solo puede elegir una opción previamente enviada al cliente." });
        return await RateEndpoints.SelectRateGroupAsync(rateId, db, httpContext, cancellationToken);
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
            || status is not (RateStatus.AcceptedByClient or RateStatus.RejectedByClient or RateStatus.Cancelled))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerInvalidRateStatus",
                message = "El vendedor solo puede aceptar, rechazar o cancelar sus cotizaciones.",
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

        // An option can never be commercially accepted while another option
        // is selected, or before the seller explicitly chooses the winner.
        var groupChoices = await db.Database.SqlQuery<string>($"""
            SELECT a.status || '|' || COALESCE(
                (SELECT chosen.rate_id::text FROM pricing.rate_quote_alternatives chosen
                 WHERE chosen.parent_rate_id = a.parent_rate_id
                   AND chosen.status = 'Selected' LIMIT 1), '') AS "Value"
            FROM pricing.rate_quote_alternatives a
            WHERE a.rate_id = {rateId}
            """).ToListAsync(cancellationToken);
        if (status == RateStatus.AcceptedByClient && groupChoices.Count > 0)
        {
            var choice = groupChoices[0].Split('|', 2);
            if (choice[0] != "Selected" || choice.Length < 2 ||
                !Guid.TryParse(choice[1], out var chosenId) || chosenId != rateId)
                return Results.Conflict(new { code = "Pricing.SellerAlternativeRequiresSelection",
                    message = "Seleccione una opción de la cotización antes de aceptarla." });
        }
        if (groupChoices.Count > 0 && groupChoices[0].Split('|', 2) is { Length: 2 } selected
            && Guid.TryParse(selected[1], out var selectedId) && selectedId != rateId)
            return Results.Conflict(new { code = "Pricing.QuoteAlternativeDiscarded",
                message = "La decisión comercial corresponde a la opción elegida del grupo." });

        // Reintentos después de guardar correctamente deben ser idempotentes.
        // QueueAsync utiliza una clave estable para no duplicar correos de Apertura.
        if (currentRate.Status == status)
        {
            if (status == RateStatus.AcceptedByClient &&
                !await openingsNotificationService.QueueAsync(rateId, RateAcceptanceActor.Seller, cancellationToken))
                return AcceptanceEmailPending(rateId);
            return Results.NoContent();
        }

        // Cancellation follows the same rule as Pricing: only an accepted quote can be cancelled.
        // Check the persisted state, not the status sent from the browser.
        // An already-cancelled quote remains idempotent in the block above.
        if (status == RateStatus.Cancelled && currentRate.Status != RateStatus.AcceptedByClient)
        {
            return Results.Conflict(new
            {
                code = "Pricing.SellerCancellationRequiresAcceptance",
                message = "Solo puede cancelar una cotización aceptada por el cliente.",
            });
        }

        if (currentRate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient or RateStatus.Expired)
            && !(status == RateStatus.Cancelled && currentRate.Status == RateStatus.AcceptedByClient))
        {
            return Results.Conflict(new
            {
                code = "Pricing.SellerRateNotAwaitingClient",
                message = "La tarifa no permite esa transición comercial: solo las cotizaciones aceptadas pueden cancelarse.",
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

        if (status == RateStatus.Cancelled && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.SellerCancellationReasonRequired",
                message = "Indique un motivo de cancelación de 1 a 1000 caracteres.",
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
                    AcceptanceObservations: acceptanceObservations
                ),
                cancellationToken
            );

            if (result.IsSuccess && status == RateStatus.AcceptedByClient)
            {
                var queued = await openingsNotificationService.QueueAsync(
                    rateId,
                    RateAcceptanceActor.Seller,
                    cancellationToken,
                    acceptanceObservations
                );
                if (!queued)
                    return AcceptanceEmailPending(rateId);
            }

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

    private static IResult AcceptanceEmailPending(Guid rateId) => Results.Json(new
    {
        code = "Pricing.AcceptanceSavedEmailPending",
        message = "La tarifa quedó aceptada, pero no se preparó el correo de Apertura. Reintente la notificación.",
        accepted = true,
        notificationQueued = false,
        rateId
    }, statusCode: StatusCodes.Status202Accepted);

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber,
        string? Observations = null
    );
}
