using System.Security.Claims;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestEndpoints
{
    private const string NotificationEventName = "notifications.notification.requested";
    private const string RateRequestCreatedNotificationType = "pricing.rate-request.created";
    private const string RateRequestWorkStatusChangedNotificationType = "pricing.rate-request.work-status.changed";

    public static IEndpointRouteBuilder MapRateRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-requests")
            .WithTags("Rate requests")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);
        // Historial solo para Pricing: las solicitudes vinculadas dejan de aparecer en /open.
        group.MapGet("/linked", GetLinkedAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapGet("/by-rate/{rateId:guid}", GetOriginalByRateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapGet("/open", GetOpenAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{id:guid}", GetByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/attach-rate", AttachRateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/complete", CompleteAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPatch("/{id:guid}/work-status", UpdateWorkStatusAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/prepare-modification", PrepareModificationAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateRateRequestRequest request,
        ServiceDbContext db,
        SellerVisibilityService visibilityService,
        AuthSellerDirectoryService sellerDirectory,
        IPricingNotificationRecipientProvider recipientProvider,
        IIntegrationEventOutboxWriter outbox,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<RateRequestPriority>(request.Priority, true, out var priority)
            || !Enum.IsDefined(priority))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.RateRequestInvalidPriority",
                message = "El tipo de tarifa debe ser Verde, Amarillo o Rojo.",
            });
        }

        if (request.SellerUserId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.RateRequestInvalidSeller",
                message = "El vendedor seleccionado no es válido.",
            });
        }

        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var sellerUserId = request.SellerUserId ?? currentUserId.Value;
        var sellerName =
            httpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.Identity?.Name;
        var sellerEmail =
            httpContext.User.FindFirst(ClaimTypes.Email)?.Value
            ?? httpContext.User.FindFirst("email")?.Value;

        if (sellerUserId != currentUserId.Value)
        {
            var visibility = await visibilityService.ResolveAsync(
                httpContext,
                requireElevated: false,
                ct
            );

            var canCreateForSeller = visibility is not null
                && (visibility.Mode == SellerVisibilityMode.All
                    || visibility.SellerUserIds.Contains(sellerUserId));

            if (!canCreateForSeller)
            {
                return Results.Forbid();
            }

            var delegatedSeller = await sellerDirectory.GetSellerAsync(sellerUserId, ct);
            if (delegatedSeller is null)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.RateRequestSellerNotAvailable",
                    message = "El vendedor seleccionado no está activo o no puede solicitar tarifas.",
                });
            }

            sellerName = delegatedSeller.DisplayName ?? delegatedSeller.UserName;
            sellerEmail = delegatedSeller.Email;
        }

        var payloadJson = request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : request.Payload.GetRawText();

        if (RateModificationRequestPayload.IsModification(payloadJson))
            return Results.BadRequest(new {
                message = "Las modificaciones de tarifas existentes deben enviarse desde la opción Solicitar modificación."
            });

        var entity = RateRequest.Create(
            priority,
            sellerUserId,
            sellerName,
            sellerEmail,
            request.ClientName,
            request.ExecutiveName,
            request.ShipmentMode,
            request.OriginName,
            request.DestinationName,
            payloadJson
        );
        entity.SetRoute(request.PoeId, request.PoeName, request.PodId, request.PodName);

        db.RateRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        // La solicitud ya quedó registrada. La notificación se encola después para que una
        // indisponibilidad temporal de Auth/Notifications no provoque duplicados por reintento
        // del vendedor. El outbox la entrega a Notifications y de ahí a SignalR y correo.
        try
        {
            await QueuePricingRealtimeNotificationAsync(
                entity,
                recipientProvider,
                outbox,
                ct
            );
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("RateRequestNotifications").LogError(
                exception,
                "La solicitud {RateRequestId} fue creada, pero no se pudieron encolar las alertas para Pricing.",
                entity.Id
            );
        }

        return Results.Ok(entity.Id);
    }

    private static async Task<IResult> GetOpenAsync(ServiceDbContext db, CancellationToken ct)
    {
        var requests = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.Status == RateRequestStatus.Open)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.RequestedAtUtc)
            .ToListAsync(ct);

        var linkedRateIds = requests
            .Where(x => x.RateId.HasValue)
            .Select(x => x.RateId!.Value)
            .Distinct()
            .ToArray();

        // A new request with an existing linked rate is no longer unassigned work.
        // This also covers PendingApproval / Open quotes awaiting margin authorization;
        // modification requests deliberately stay visible until their new revision is sent.
        var linkedExistingRateIds = linkedRateIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.RateHeaders
                .AsNoTracking()
                .Where(x => linkedRateIds.Contains(x.Id) && !x.IsDeleted)
                .Select(x => x.Id)
                .ToListAsync(ct))
                .ToHashSet();

        return Results.Ok(requests
            .Where(x => RateModificationRequestPayload.IsModification(x.PayloadJson)
                || !x.RateId.HasValue || !linkedExistingRateIds.Contains(x.RateId.Value))
            .Select(ToDto)
            .ToArray());
    }

    // Keep the original sales snapshot intact; never reconstruct it from the quote.
    private static async Task<IResult> GetOriginalByRateAsync(
        Guid rateId, ServiceDbContext db, CancellationToken ct)
    {
        var exists = await db.RateHeaders.AsNoTracking()
            .AnyAsync(x => x.Id == rateId && !x.IsDeleted, ct);
        if (!exists) return Results.NotFound();

        var linked = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderBy(x => x.RequestedAtUtc)
            .ToListAsync(ct);
        var original = linked.FirstOrDefault(x =>
            !RateModificationRequestPayload.IsModification(x.PayloadJson));
        return original is null ? Results.NotFound() : Results.Ok(ToDto(original));
    }

    // A compact historical index. It never includes the full request payload or
    // unlinked work items and does not change the existing open queue.
    private static async Task<IResult> GetLinkedAsync(
        ServiceDbContext db, CancellationToken ct)
    {
        var requests = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId.HasValue)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(1000)
            .ToListAsync(ct);
        var ids = requests.Select(x => x.RateId!.Value).Distinct().ToArray();
        var rates = await db.RateHeaders.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.QuoNumber, x.RateCode })
            .ToDictionaryAsync(x => x.Id, ct);

        return Results.Ok(requests
            .Where(x => x.RateId.HasValue && rates.ContainsKey(x.RateId.Value)
                && !RateModificationRequestPayload.IsModification(x.PayloadJson))
            .Take(300)
            .Select(x => new
            {
                x.Id,
                x.RateId,
                quoNumber = rates[x.RateId!.Value].QuoNumber
                    ?? rates[x.RateId.Value].RateCode,
                x.SellerName,
                x.ClientName,
                x.ExecutiveName,
                x.ShipmentMode,
                x.OriginName,
                x.DestinationName,
                x.RequestedAtUtc,
                status = x.Status.ToString(),
                priority = x.Priority.ToString(),
            }).ToArray());
    }

    private static async Task<IResult> GetByIdAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        var request = await db.RateRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return request is null ? Results.NotFound() : Results.Ok(ToDto(request));
    }

    private static async Task<IResult> AttachRateAsync(
        Guid id,
        AttachRateRequestRequest request,
        ServiceDbContext db,
        HttpContext context,
        CancellationToken ct
    )
    {
        if (request.RateId == Guid.Empty)
            return Results.BadRequest(new { message = "La tarifa es requerida." });

        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status != RateRequestStatus.Open)
            return Results.Conflict(new { message = "La solicitud ya no está abierta." });
        if (!RateRequestLeaseEndpoints.HasActiveLease(context, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        var rateExists = await db.RateHeaders.AnyAsync(x => x.Id == request.RateId && !x.IsDeleted, ct);
        if (!rateExists) return Results.BadRequest(new { message = "La tarifa indicada no existe." });

        if (RateModificationRequestPayload.IsModification(entity.PayloadJson))
            return Results.Conflict(new { message = "Una modificación ya está vinculada a la tarifa original." });
        entity.AttachRate(request.RateId);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // DueAtUtc es el plazo SLA y no debe impedir completar una solicitud atrasada.
    // El cierre manual sólo cambia el estado; complete-rate envía la cotización PDF.
    private static async Task<IResult> CompleteAsync(
        Guid id,
        ServiceDbContext db,
        HttpContext context,
        CancellationToken ct
    )
    {
        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (RateModificationRequestPayload.IsModification(entity.PayloadJson))
            return Results.Conflict(new {
                code = "Pricing.ModificationExplicitDeliveryRequired",
                message = "Salir o liberar la reserva no completa una modificación; sólo Enviar revisión al vendedor puede finalizarla."
            });
        if (entity.Status == RateRequestStatus.Completed) return Results.NoContent();
        if (entity.Status != RateRequestStatus.Open)
            return Results.Conflict(new { message = "Sólo una solicitud abierta puede completarse." });
        if (!RateRequestLeaseEndpoints.HasActiveLease(context, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        entity.MarkCompleted(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelRateRequestRequest request,
        ServiceDbContext db,
        HttpContext context,
        CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null || actor == Guid.Empty) return Results.Unauthorized();

        var reason = request.Reason?.Trim() ?? "";
        if (reason is not ("Duplicate" or "CustomerCancelled" or "SellerCancelled" or "Other"))
            return Results.BadRequest(new { code = "Pricing.RateRequestInvalidCancellationReason",
                message = "Seleccione un motivo válido para cancelar la solicitud." });

        var details = request.Details?.Trim();
        if (details?.Length > 500 || (reason == "Other" && string.IsNullOrWhiteSpace(details)))
            return Results.BadRequest(new { code = "Pricing.RateRequestInvalidCancellationDetails",
                message = "Indique los detalles del motivo (máximo 500 caracteres)." });

        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status == RateRequestStatus.Cancelled) return Results.NoContent();
        if (entity.Status != RateRequestStatus.Open)
            return Results.Conflict(new { message = "Sólo pueden cancelarse solicitudes pendientes." });
        if (!RateRequestLeaseEndpoints.HasActiveLease(context, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        var actorName = context.User.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User.FindFirst("name")?.Value
            ?? context.User.Identity?.Name
            ?? actor.Value.ToString();
        actorName = actorName.Trim();
        if (actorName.Length > 200) actorName = actorName[..200];

        // Cancellation only closes the seller's request. Never delete a generated RateHeader.
        entity.MarkCancelled(reason, details, actor.Value, actorName, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    internal static async Task QueuePricingRealtimeNotificationAsync(
        RateRequest request,
        IPricingNotificationRecipientProvider recipientProvider,
        IIntegrationEventOutboxWriter outbox,
        CancellationToken cancellationToken
    )
    {
        var recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        if (recipients.Count == 0)
            return;

        var equipmentType = ExtractEquipmentType(request.PayloadJson, request.ShipmentMode);
        var route = BuildRoute(request);
        var seller = request.SellerName ?? request.ExecutiveName ?? "Ventas";
        var client = request.ClientName ?? "Cliente sin definir";
        var equipmentText = equipmentType ?? request.ShipmentMode ?? "Equipo sin definir";
        var modification = RateModificationRequestPayload.IsModification(request.PayloadJson);
        var subject = modification ? "Nueva solicitud de modificación de tarifa" : "Nueva solicitud de tarifa de Ventas";
        var body = modification
            ? $"{seller} solicitó cambios en la tarifa de {client}. Ruta: {route}. Revise los cambios y genere una nueva revisión."
            : $"{seller} envió una solicitud para {client}. Ruta: {route}. Contenedor/equipo: {equipmentText}.";

        var payload = new
        {
            type = RateRequestCreatedNotificationType,
            rateRequestId = request.Id,
            priority = request.Priority.ToString(),
            request.ShipmentMode,
            equipmentType,
            request.ClientName,
            request.SellerUserId,
            request.SellerName,
            request.ExecutiveName,
            request.OriginName,
            request.DestinationName,
            request.PoeId,
            request.PoeName,
            request.PodId,
            request.PodName,
            request.RequestedAtUtc,
            request.DueAtUtc,
            action = "continue-rate-request",
            route = $"/pricing/rate-requests/{request.Id}",
        };

        foreach (var recipient in recipients.Where(x => x.UserId != Guid.Empty))
        {
            var systemNotificationRequest = new
            {
                notificationType = RateRequestCreatedNotificationType,
                templateCode = (string?)null,
                channel = "System",
                entityType = "RateRequest",
                entityId = request.Id.ToString(),
                subject,
                body,
                payload,
                maxAttempts = 3,
                recipients = new[]
                {
                    new
                    {
                        userId = recipient.UserId.ToString(),
                        address = recipient.UserId.ToString(),
                        displayName = recipient.DisplayName ?? recipient.UserName,
                    },
                },
            };

            await outbox.WriteAsync(
                NotificationEventName,
                NotificationEventName,
                systemNotificationRequest,
                correlationId: $"pricing-rate-request:{request.Id:N}:system:{recipient.UserId:N}",
                cancellationToken: cancellationToken
            );

            if (string.IsNullOrWhiteSpace(recipient.Email))
                continue;

            var emailNotificationRequest = new
            {
                notificationType = RateRequestCreatedNotificationType,
                templateCode = (string?)null,
                channel = "Email",
                entityType = "RateRequest",
                entityId = request.Id.ToString(),
                subject,
                body,
                payload,
                maxAttempts = 3,
                recipients = new[]
                {
                    new
                    {
                        userId = recipient.UserId.ToString(),
                        address = recipient.Email,
                        displayName = recipient.DisplayName ?? recipient.UserName,
                    },
                },
            };

            await outbox.WriteAsync(
                NotificationEventName,
                NotificationEventName,
                emailNotificationRequest,
                correlationId: $"pricing-rate-request:{request.Id:N}:email:{recipient.UserId:N}",
                cancellationToken: cancellationToken
            );
        }
    }

    private static string BuildRoute(RateRequest request)
    {
        var parts = new[]
        {
            request.OriginName,
            request.PoeName,
            request.PodName,
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        if (parts.Length > 0)
            return string.Join(" → ", parts);

        return request.DestinationName is { Length: > 0 }
            ? $"{request.OriginName ?? "Origen"} → {request.DestinationName}"
            : request.OriginName ?? "Ruta sin definir";
    }

    private static string? ExtractEquipmentType(string payloadJson, string? shipmentMode)
    {
        if (string.Equals(shipmentMode, "LCL", StringComparison.OrdinalIgnoreCase))
            return "LCL";

        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "form", out var form)
                || form.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var equipmentType = GetString(form, "equipmentType");
            var equipmentSize = GetString(form, "equipmentSize");

            if (string.IsNullOrWhiteSpace(equipmentType))
                return string.IsNullOrWhiteSpace(equipmentSize) ? null : equipmentSize;
            if (string.IsNullOrWhiteSpace(equipmentSize)
                || equipmentType.Contains(equipmentSize, StringComparison.OrdinalIgnoreCase))
            {
                return equipmentType;
            }

            return $"{equipmentSize} {equipmentType}".Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    // Opening a request, claiming its lease or approving its existing quote never
    // implies the seller's requested changes were applied. Only a successful
    // wizard save may call this endpoint.
    private static async Task<IResult> PrepareModificationAsync(
        Guid id,
        PrepareRateModificationRequest request,
        ServiceDbContext db,
        HttpContext context,
        CancellationToken ct)
    {
        if (request.RateId == Guid.Empty || request.RevisionNumber <= 0)
            return Results.BadRequest(new { message = "Indique la tarifa y la revisión guardada." });

        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status != RateRequestStatus.Open
            || !RateModificationRequestPayload.IsModification(entity.PayloadJson)
            || entity.RateId != request.RateId)
            return Results.Conflict(new {
                code = "Pricing.RateModificationNotOpen",
                message = "La solicitud debe ser una modificación abierta de la misma tarifa."
            });
        if (!RateRequestLeaseEndpoints.HasActiveLease(context, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        var revision = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == request.RateId && !x.IsDeleted)
            .Select(x => (int?)x.RevisionNumber)
            .FirstOrDefaultAsync(ct);

        if (revision is null || revision != request.RevisionNumber
            || revision <= RateModificationRequestPayload.SourceRevisionNumber(entity.PayloadJson))
            return Results.Conflict(new {
                code = "Pricing.RateModificationRevisionRequired",
                message = "Guarde la nueva revisión de esta tarifa antes de habilitar el envío al vendedor."
            });

        entity.PrepareModification(revision.Value);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { entity.PreparedRevisionNumber });
    }

    private static async Task<IResult> UpdateWorkStatusAsync(
        Guid id,
        UpdateWorkStatusRequest request,
        ServiceDbContext db,
        HttpContext context,
        IIntegrationEventOutboxWriter outbox,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (request.WorkStatus is not ("Working" or "RequestedFromProvider"))
            return Results.BadRequest(new { code = "Pricing.InvalidRateRequestWorkStatus", message = "Seleccione Trabajando o Solicitado a proveedor." });

        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status != RateRequestStatus.Open)
            return Results.Conflict(new { message = "La solicitud ya no está abierta." });
        if (!RateRequestLeaseEndpoints.HasActiveLease(context, entity))
            return RateRequestLeaseEndpoints.LeaseRequired();

        entity.SetWorkStatus(request.WorkStatus, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);

        // Solo notificación interna (SignalR) al vendedor titular; no envía correo.
        // El estado ya quedó guardado, de modo que un fallo de Notifications
        // nunca revierte el cambio ni duplica la solicitud.
        if (entity.SellerUserId is { } sellerId && sellerId != Guid.Empty)
        {
            var label = entity.WorkStatus == "RequestedFromProvider"
                ? "Solicitado a proveedor"
                : "Trabajando";
            var message = new
            {
                notificationType = RateRequestWorkStatusChangedNotificationType,
                templateCode = (string?)null,
                channel = "System",
                entityType = "RateRequest",
                entityId = entity.Id.ToString(),
                subject = $"Estado de solicitud: {label}",
                body = $"La solicitud de {entity.ClientName ?? "su cliente"} cambió a {label}.",
                payload = new
                {
                    type = RateRequestWorkStatusChangedNotificationType,
                    rateRequestId = entity.Id,
                    entity.WorkStatus,
                    entity.WorkStatusUpdatedAtUtc,
                    route = "/home",
                },
                maxAttempts = 3,
                recipients = new[]
                {
                    new
                    {
                        userId = sellerId.ToString(),
                        address = sellerId.ToString(),
                        displayName = entity.SellerName ?? "Vendedor",
                    },
                },
            };

            try
            {
                await outbox.WriteAsync(
                    NotificationEventName,
                    NotificationEventName,
                    message,
                    correlationId: $"pricing-request-work-status:{id:N}:{entity.WorkStatus}:{entity.WorkStatusUpdatedAtUtc:yyyyMMddHHmmssfff}",
                    cancellationToken: ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception error)
            {
                loggerFactory.CreateLogger("RateRequestWorkStatusNotifications").LogError(
                    error, "No se pudo notificar el cambio de estado de la solicitud {RateRequestId}.", id);
            }
        }

        return Results.Ok(new { entity.WorkStatus, entity.WorkStatusUpdatedAtUtc });
    }

    private static object ToDto(RateRequest request)
    {
        using var document = JsonDocument.Parse(request.PayloadJson);
        return new
        {
            request.Id,
            kind = RateModificationRequestPayload.IsModification(request.PayloadJson) ? "Modification" : "New",
            priority = request.Priority.ToString(),
            status = request.Status.ToString(),
            request.WorkStatus,
            request.WorkStatusUpdatedAtUtc,
            request.PreparedRevisionNumber,
            request.RequestedAtUtc,
            request.DueAtUtc,
            request.CompletedAtUtc,
            request.CancelledAtUtc,
            request.CancelledByUserId,
            request.CancelledByName,
            request.CancellationReason,
            request.CancellationDetails,
            request.SlaReminderSentAtUtc,
            request.RateId,
            request.SellerUserId,
            request.SellerName,
            request.SellerEmail,
            request.ClientName,
            request.ExecutiveName,
            request.ShipmentMode,
            equipmentType = ExtractEquipmentType(request.PayloadJson, request.ShipmentMode),
            request.OriginName,
            request.DestinationName,
            request.PoeId,
            request.PoeName,
            request.PodId,
            request.PodName,
            lockedByUserId = request.LockExpiresAtUtc > DateTime.UtcNow ? request.LockedByUserId : null,
            lockedByName = request.LockExpiresAtUtc > DateTime.UtcNow ? request.LockedByName : null,
            lockExpiresAtUtc = request.LockExpiresAtUtc > DateTime.UtcNow ? request.LockExpiresAtUtc : null,
            payload = document.RootElement.Clone(),
        };
    }

    private sealed record PrepareRateModificationRequest(Guid RateId, int RevisionNumber);
    private sealed record UpdateWorkStatusRequest(string? WorkStatus);
    private sealed record CancelRateRequestRequest(string? Reason, string? Details);

    private sealed record CreateRateRequestRequest(
        string Priority,
        string? ClientName,
        string? ExecutiveName,
        string? ShipmentMode,
        string? OriginName,
        string? DestinationName,
        Guid? PoeId,
        string? PoeName,
        Guid? PodId,
        string? PodName,
        Guid? SellerUserId,
        JsonElement Payload
    );

    private sealed record AttachRateRequestRequest(Guid RateId);
}
