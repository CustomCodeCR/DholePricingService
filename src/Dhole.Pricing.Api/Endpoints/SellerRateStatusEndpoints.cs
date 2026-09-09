using System.Net;
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
            .Include(x => x.RateServices)
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
            await NotifyOpeningsAsync(emailService, loggerFactory, rateRequest, currentRate, cancellationToken);
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
            await NotifyOpeningsAsync(emailService, loggerFactory, rateRequest, currentRate, cancellationToken);
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task NotifyOpeningsAsync(
        PricingEmailService emailService,
        ILoggerFactory loggerFactory,
        RateRequest request,
        RateHeader rate,
        CancellationToken cancellationToken)
    {
        var seller = Html(request.SellerName ?? request.ExecutiveName ?? rate.ExecutiveName ?? "Ventas");
        var client = Html(rate.ClientName ?? request.ClientName ?? "Cliente");
        var quoteNumber = rate.QuoNumber ?? rate.RateCode;
        var quote = Html(quoteNumber);
        var pol = Html(rate.PolName);
        var poe = Html(rate.PoeName);
        var pod = Html(rate.PodName ?? "—");
        var route = $"{pol} → {poe} → {pod}";
        var incoterm = Html(rate.IncotermName ?? rate.IncotermCode ?? "—");
        var operationType = Html(OperationTypeLabel(rate.OperationType));

        var containers = rate.RateContainers.Count == 0
            ? Html($"{rate.ContainerTypeName} ({rate.ContainerTypeCode}) · Cantidad: {rate.ContainerQuantity}")
            : string.Join("<br />", rate.RateContainers
                .OrderBy(x => x.ContainerTypeName)
                .Select(x => Html($"{x.ContainerTypeName} ({x.ContainerTypeCode}) · Cantidad: {x.Quantity}")));

        var services = rate.RateServices.Count == 0
            ? "—"
            : string.Join("<br />", rate.RateServices
                .OrderBy(x => x.ServiceName)
                .Select(x => Html(string.IsNullOrWhiteSpace(x.ServiceCode)
                    ? x.ServiceName
                    : $"{x.ServiceName} ({x.ServiceCode})")));

        try
        {
            await emailService.SendAsync(
                OpeningsEmail,
                $"Tarifa aprobada por cliente - {rate.ClientName ?? request.ClientName ?? quoteNumber}",
                $"""
                <p>Se confirmó una tarifa aprobada por el cliente.</p>
                <table style="border-collapse:collapse; width:100%; max-width:760px">
                  <tr><td style="padding:5px 10px"><strong>Cliente</strong></td><td style="padding:5px 10px">{client}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Ruta (POL → POE → POD)</strong></td><td style="padding:5px 10px">{route}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Contenedor / Equipo</strong></td><td style="padding:5px 10px">{containers}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Incoterm</strong></td><td style="padding:5px 10px">{incoterm}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Vendedor</strong></td><td style="padding:5px 10px">{seller}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>QUO</strong></td><td style="padding:5px 10px">{quote}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Servicios</strong></td><td style="padding:5px 10px">{services}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Tipo de operación</strong></td><td style="padding:5px 10px">{operationType}</td></tr>
                </table>
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
                rate.Id
            );
        }
    }

    private static string OperationTypeLabel(RateOperationType operationType) => operationType switch
    {
        RateOperationType.Import => "Importación",
        RateOperationType.Export => "Exportación",
        RateOperationType.TransitDomestic => "Tránsito / doméstico",
        _ => operationType.ToString(),
    };

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private sealed record SellerRateStatusRequest(
        string Status,
        string? Reason,
        string? IdtraNumber
    );
}
