using System.Net;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Auditing;
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
    private const string OpeningsEmail = "aperturas@grupocastrofallas.com";

    public static IEndpointRouteBuilder MapSellerRateStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/pricing/seller-rates/{rateId:guid}/status", SetStatusAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        return app;
    }

    private static async Task<IResult> SetStatusAsync(
        Guid rateId,
        SellerRateStatusRequest request,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        IPricingAuditService audit,
        IRateHeaderCacheService cache,
        PricingEmailService emailService,
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

        if (rateRequest is null)
            return Results.Forbid();

        var currentRate = await db.RateHeaders
            .Include(x => x.RateContainers)
            .Include(x => x.RateDetails)
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

        if (currentRate is null)
            return Results.NotFound();

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

        // Ventas confirma la decisión del cliente, pero normalmente aún no dispone del IDTRA.
        // Si la tarifa todavía no lo tiene, registramos la aceptación comercial sin inventar un
        // identificador temporal. El IDTRA puede incorporarse posteriormente por el flujo operativo.
        if (status == RateStatus.AcceptedByClient && string.IsNullOrWhiteSpace(currentRate.IdtraNumber))
        {
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
            await NotifyOpeningsAsync(emailService, loggerFactory, rateRequest, currentRate.QuoNumber ?? currentRate.RateCode, cancellationToken);
            return Results.NoContent();
        }

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                null,
                currentUserId
            ),
            cancellationToken
        );

        if (result.IsSuccess && status == RateStatus.AcceptedByClient)
        {
            await NotifyOpeningsAsync(emailService, loggerFactory, rateRequest, currentRate.QuoNumber ?? currentRate.RateCode, cancellationToken);
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task NotifyOpeningsAsync(
        PricingEmailService emailService,
        ILoggerFactory loggerFactory,
        Dhole.Pricing.Domain.Rates.Entities.RateRequest request,
        string quoteNumber,
        CancellationToken cancellationToken)
    {
        var seller = WebUtility.HtmlEncode(request.SellerName ?? request.ExecutiveName ?? "Ventas");
        var client = WebUtility.HtmlEncode(request.ClientName ?? "Cliente");
        var quote = WebUtility.HtmlEncode(quoteNumber);
        var route = WebUtility.HtmlEncode(string.Join(" → ", new[] { request.OriginName, request.PoeName, request.PodName, request.DestinationName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)));

        try
        {
            await emailService.SendAsync(
                OpeningsEmail,
                $"Tarifa aprobada por cliente - {request.ClientName ?? quoteNumber}",
                $"""
                <p>Se confirmó una tarifa aprobada por el cliente.</p>
                <p><strong>Cliente:</strong> {client}<br />
                <strong>Tarifa:</strong> {quote}<br />
                <strong>Vendedor:</strong> {seller}<br />
                <strong>Ruta:</strong> {route}</p>
                <p>Favor continuar con el proceso de apertura correspondiente.</p>
                """,
                null,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La aceptación comercial ya ocurrió; un fallo de correo no debe revertirla.
            loggerFactory.CreateLogger("SellerRateOpeningsEmail").LogError(
                exception,
                "No se pudo notificar a Aperturas la aceptación de la tarifa {RateId}.",
                request.RateId
            );
        }
    }

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber
    );
}
