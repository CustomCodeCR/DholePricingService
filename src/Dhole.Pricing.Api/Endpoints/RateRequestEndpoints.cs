using System.Security.Claims;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
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

    public static IEndpointRouteBuilder MapRateRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-requests")
            .WithTags("Rate requests")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);
        group.MapGet("/open", GetOpenAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{id:guid}", GetByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/attach-rate", AttachRateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateRateRequestRequest request,
        ServiceDbContext db,
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

        var payloadJson = request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : request.Payload.GetRawText();

        var sellerName =
            httpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.Identity?.Name;
        var sellerEmail =
            httpContext.User.FindFirst(ClaimTypes.Email)?.Value
            ?? httpContext.User.FindFirst("email")?.Value;

        var entity = RateRequest.Create(
            priority,
            httpContext.GetCurrentUserId(),
            sellerName,
            sellerEmail,
            request.ClientName,
            request.ExecutiveName,
            request.ShipmentMode,
            request.OriginName,
            request.DestinationName,
            payloadJson
        );

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
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.RequestedAtUtc)
            .ToListAsync(ct);

        var linkedRateIds = requests
            .Where(x => x.RateId.HasValue)
            .Select(x => x.RateId!.Value)
            .Distinct()
            .ToArray();

        var completedRateIds = linkedRateIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.RateHeaders
                .AsNoTracking()
                .Where(x => linkedRateIds.Contains(x.Id)
                    && (x.Status == RateStatus.Sent
                        || x.Status == RateStatus.AcceptedByClient
                        || x.Status == RateStatus.RejectedByClient
                        || x.Status == RateStatus.Closed))
                .Select(x => x.Id)
                .ToListAsync(ct))
                .ToHashSet();

        return Results.Ok(requests
            .Where(x => !x.RateId.HasValue || !completedRateIds.Contains(x.RateId.Value))
            .Select(ToDto)
            .ToArray());
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
        CancellationToken ct
    )
    {
        if (request.RateId == Guid.Empty)
            return Results.BadRequest(new { message = "La tarifa es requerida." });

        var entity = await db.RateRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status != RateRequestStatus.Open)
            return Results.Conflict(new { message = "La solicitud ya no está abierta." });

        var rateExists = await db.RateHeaders.AnyAsync(x => x.Id == request.RateId && !x.IsDeleted, ct);
        if (!rateExists) return Results.BadRequest(new { message = "La tarifa indicada no existe." });

        entity.AttachRate(request.RateId);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task QueuePricingRealtimeNotificationAsync(
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
        var route = $"{request.OriginName ?? "Origen"} → {request.DestinationName ?? "Destino"}";
        var seller = request.SellerName ?? request.ExecutiveName ?? "Ventas";
        var client = request.ClientName ?? "Cliente sin definir";
        var equipmentText = equipmentType ?? request.ShipmentMode ?? "Equipo sin definir";
        var subject = "Nueva solicitud de tarifa de Ventas";
        var body = $"{seller} envió una solicitud para {client}. Ruta: {route}. Contenedor/equipo: {equipmentText}.";

        var payload = new
        {
            type = RateRequestCreatedNotificationType,
            rateRequestId = request.Id,
            priority = request.Priority.ToString(),
            request.ShipmentMode,
            equipmentType,
            request.ClientName,
            request.SellerName,
            request.ExecutiveName,
            request.OriginName,
            request.DestinationName,
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

    private static object ToDto(RateRequest request)
    {
        using var document = JsonDocument.Parse(request.PayloadJson);
        return new
        {
            request.Id,
            priority = request.Priority.ToString(),
            status = request.Status.ToString(),
            request.RequestedAtUtc,
            request.DueAtUtc,
            request.CompletedAtUtc,
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
            payload = document.RootElement.Clone(),
        };
    }

    private sealed record CreateRateRequestRequest(
        string Priority,
        string? ClientName,
        string? ExecutiveName,
        string? ShipmentMode,
        string? OriginName,
        string? DestinationName,
        JsonElement Payload
    );

    private sealed record AttachRateRequestRequest(Guid RateId);
}
