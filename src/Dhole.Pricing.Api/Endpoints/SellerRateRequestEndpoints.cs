using System.Security.Claims;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateRequestEndpoints
{
    public static IEndpointRouteBuilder MapSellerRateRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/rate-requests/mine", GetMineAsync)
            .WithTags("Seller rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/rate-requests/mine/{id:guid}", GetMineByIdAsync)
            .WithTags("Seller rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapPut("/api/pricing/rate-requests/mine/{id:guid}", UpdateMineAsync)
            .WithTags("Seller rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapPost("/api/pricing/seller-rates/{rateId:guid}/modification-requests", CreateModificationAsync)
            .WithTags("Seller rate requests")
            .RequireIdempotency()
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapModificationSource();

        app.MapGet("/api/pricing/rate-requests/dashboard", GetAdminDashboardAsync)
            .WithTags("Seller dashboards")
            .RequireAuthorization();

        return app;
    }



    public static IEndpointRouteBuilder MapModificationSource(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/seller-rates/{rateId:guid}/modification-source", GetModificationSourceAsync)
            .WithTags("Seller rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);
        return app;
    }

    private static async Task<IResult> GetModificationSourceAsync(
        Guid rateId, ServiceDbContext db, SellerRateAccessService access,
        HttpContext context, CancellationToken ct)
    {
        var userId = context.GetCurrentUserId();
        if (userId is null || userId == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking().Include(x => x.RateServices)
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, ct);
        if (rate is null) return Results.NotFound();
        if (!await access.CanReadAsync(rate, userId.Value, ct)) return Results.Forbid();
        if (!RateModificationWizardSnapshot.IsEligible(rate))
            return Results.Conflict(new { message = "Esta tarifa no admite solicitudes de modificación." });

        // Original seller request owns the wizard 1–4 values. Pricing's generated
        // RateHeader is still the authoritative baseline for the later difference.
        var linked = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderBy(x => x.RequestedAtUtc)
            .Select(x => new { x.PayloadJson, x.Priority })
            .ToListAsync(ct);
        JsonElement? originalRequest = null;
        string? priority = null;
        foreach (var entry in linked)
        {
            if (RateModificationRequestPayload.IsModification(entry.PayloadJson)) continue;
            try
            {
                using var document = JsonDocument.Parse(entry.PayloadJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("form", out var requestForm)
                    || requestForm.ValueKind != JsonValueKind.Object)
                    continue;
                // Keep the first original request, even after later amendments.
                originalRequest = root.Clone();
                priority = entry.Priority.ToString();
                break;
            }
            catch (JsonException) { }
        }

        JsonElement? PayloadField(string name)
            => originalRequest is { ValueKind: JsonValueKind.Object } source
                && source.TryGetProperty(name, out var value) ? value.Clone() : null;

        return Results.Ok(new
        {
            rateId = rate.Id,
            reference = rate.QuoNumber ?? rate.RateCode,
            revisionNumber = rate.RevisionNumber,
            priority = priority ?? "Green",
            form = (object)(PayloadField("form") ?? JsonSerializer.SerializeToElement(
                RateModificationWizardSnapshot.OriginalForm(rate))),
            pickupLocations = (object)(PayloadField("pickupLocations") ??
                JsonSerializer.SerializeToElement(RateModificationWizardSnapshot.PickupLocations(rate))),
            supportEntityId = PayloadField("supportEntityId"),
            supportDocuments = PayloadField("supportDocuments"),
            sellerContext = RateModificationWizardSnapshot.SellerCargoContext(
                rate, PayloadField("sellerContext")),
            names = RateModificationWizardSnapshot.Names(rate),
        });
    }

    private static async Task<IResult> CreateModificationAsync(
        Guid rateId, CreateSellerRateModificationRequest request,
        ServiceDbContext db, SellerRateAccessService sellerAccess,
        AuthSellerDirectoryService sellerDirectory,
        IPricingNotificationRecipientProvider recipientProvider,
        IIntegrationEventOutboxWriter outbox, ILoggerFactory loggerFactory,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var sellerId = httpContext.GetCurrentUserId();
        if (sellerId is null || sellerId == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.Include(x => x.RateServices)
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, sellerId.Value, cancellationToken))
            return Results.Forbid();
        if (!RateModificationWizardSnapshot.IsEligible(rate))
            return Results.Conflict(new { message = "Esta tarifa no admite solicitudes de modificación." });
        if (!Enum.TryParse<RateRequestPriority>(request.Priority, true, out var priority) || !Enum.IsDefined(priority))
            return Results.BadRequest(new { message = "Seleccione una prioridad válida." });
        if (request.Payload.ValueKind != JsonValueKind.Object
            || !request.Payload.TryGetProperty("form", out var form)
            || form.ValueKind != JsonValueKind.Object
            || request.Payload.GetRawText().Length > 250000)
            return Results.BadRequest(new { message = "Envíe el formulario del wizard de Ventas (pantallas 1 a 4)." });
        if (!request.Payload.TryGetProperty("sourceRevisionNumber", out var sourceRevision)
            || !sourceRevision.TryGetInt32(out var originalRevision)
            || originalRevision != rate.RevisionNumber)
            return Results.Conflict(new {
                code = "Pricing.RateModificationStaleSource",
                message = "La tarifa cambió después de abrir el wizard. Actualice los datos y vuelva a comparar."
            });

        // Compare the requested cargo against the real quote; fall back to the
        // original seller request when older quotes have no persisted cargo lines.
        JsonElement? originalSellerContext = null;
        var relatedPayloads = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderBy(x => x.RequestedAtUtc)
            .Select(x => x.PayloadJson)
            .ToListAsync(cancellationToken);
        foreach (var json in relatedPayloads)
        {
            if (RateModificationRequestPayload.IsModification(json)) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("sellerContext", out var context)
                    && context.ValueKind == JsonValueKind.Object)
                {
                    originalSellerContext = context.Clone();
                    break;
                }
            }
            catch (JsonException) { }
        }

        // Serialised in the same row as the existing RateRequest. The original tariff is unchanged.
        var comparison = RateModificationWizardSnapshot.Compare(
            rate, request.Payload, originalSellerContext);
        if (comparison.Count == 0)
            return Results.BadRequest(new { message = "No hay diferencias entre la tarifa original y los cambios del wizard." });

        var active = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId && x.Status == RateRequestStatus.Open)
            .Select(x => new { x.Id, x.PayloadJson }).ToListAsync(cancellationToken);
        var pending = active.FirstOrDefault(x => RateModificationRequestPayload.IsModification(x.PayloadJson));
        if (pending is not null)
            return Results.Conflict(new {
                code = "Pricing.RateModificationAlreadyOpen",
                message = "Esta tarifa ya tiene una solicitud de modificación pendiente.",
                requestId = pending.Id
            });

        var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
            ?? httpContext.User.FindFirst("name")?.Value ?? httpContext.User.Identity?.Name;
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
            ?? httpContext.User.FindFirst("email")?.Value;
        var original = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId && x.SellerUserId == sellerId)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        userName ??= original?.SellerName;
        email ??= original?.SellerEmail;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(userName))
        {
            var directoryUser = await sellerDirectory.GetSalesExecutiveAsync(sellerId.Value, cancellationToken)
                ?? await sellerDirectory.GetSellerAsync(sellerId.Value, cancellationToken);
            email = !string.IsNullOrWhiteSpace(email) ? email : directoryUser?.Email;
            userName = !string.IsNullOrWhiteSpace(userName) ? userName
                : directoryUser?.DisplayName ?? directoryUser?.UserName;
        }
        if (string.IsNullOrWhiteSpace(email))
            return Results.BadRequest(new { message = "No fue posible identificar el correo del vendedor en Auth." });

        var payload = JsonSerializer.Serialize(new {
            kind = "Modification",
            modification = new {
                sourceRevisionNumber = rate.RevisionNumber,
                quote = rate.QuoNumber ?? rate.RateCode,
                // Explicit camelCase also inside PayloadJson (JsonElement is returned
                // without applying ASP.NET's response naming policy).
                changes = comparison.Select(change => new {
                    field = change.Field,
                    action = change.Action,
                    original = change.Original,
                    requested = change.Requested
                }).ToArray(),
                original = RateModificationWizardSnapshot.OriginalForm(rate),
            },
            form = form.Clone(),
            pickupLocations = request.Payload.TryGetProperty("pickupLocations", out var pickups)
                ? pickups.Clone() : default(JsonElement?),
            supportEntityId = request.Payload.TryGetProperty("supportEntityId", out var supportId)
                ? supportId.Clone() : default(JsonElement?),
            supportDocuments = request.Payload.TryGetProperty("supportDocuments", out var documents)
                ? documents.Clone() : default(JsonElement?),
            sellerContext = request.Payload.TryGetProperty("sellerContext", out var sellerContext)
                ? sellerContext.Clone() : default(JsonElement?),
            requestContext = request.Payload.TryGetProperty("requestContext", out var requestContext)
                ? requestContext.Clone() : default(JsonElement?),
        });

        var entity = RateRequest.Create(priority, sellerId, userName, email,
            rate.ClientName, rate.ExecutiveName, rate.ShipmentMode.ToString(),
            rate.PolName, rate.PodName ?? rate.PoeName, payload);
        entity.SetRoute(rate.PoeId, rate.PoeName, rate.PodId, rate.PodName);
        entity.AttachRate(rate.Id);
        db.RateRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await RateRequestEndpoints.QueuePricingRealtimeNotificationAsync(
                entity, recipientProvider, outbox, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error)
        {
            loggerFactory.CreateLogger("RateModificationNotifications").LogError(
                error, "La solicitud {RequestId} fue creada, pero su alerta no pudo enviarse.", entity.Id);
        }
        return Results.Ok(new { id = entity.Id, rateId = rate.Id, status = entity.Status.ToString(),
            changes = comparison.Count });
    }

    private sealed record CreateSellerRateModificationRequest(string Priority, JsonElement Payload);

    private static async Task<IResult> GetMineAsync(
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        var requests = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.SellerUserId == currentUserId.Value)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(requests.Select(ToDto).ToArray());
    }

    private static async Task<IResult> GetMineByIdAsync(
        Guid id,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        var request = await db.RateRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.SellerUserId == currentUserId.Value, cancellationToken);
        return request is null ? Results.NotFound() : Results.Ok(ToDto(request));
    }

    private static async Task<IResult> UpdateMineAsync(
        Guid id,
        UpdateSellerRateRequest request,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        // Never accept an owner override from the caller. Sellers edit only their own requests.
        var entity = await db.RateRequests
            .FirstOrDefaultAsync(x => x.Id == id && x.SellerUserId == currentUserId.Value, cancellationToken);
        if (entity is null) return Results.NotFound();

        if (entity.Status != Dhole.Pricing.Domain.Rates.Enums.RateRequestStatus.Open || entity.RateId.HasValue)
            return Results.Conflict(new { message = "La solicitud ya está siendo procesada o fue completada y no puede editarse." });

        if (entity.LockExpiresAtUtc > DateTime.UtcNow)
            return Results.Conflict(new {
                code = "Pricing.RateRequestAlreadyClaimed",
                message = $"La solicitud está siendo atendida por {entity.LockedByName ?? "Pricing"}. Espere a que liberen la reserva.",
                ownerName = entity.LockedByName,
                expiresAtUtc = entity.LockExpiresAtUtc,
            });

        if (!Enum.TryParse<Dhole.Pricing.Domain.Rates.Enums.RateRequestPriority>(
                request.Priority, true, out var priority) || !Enum.IsDefined(priority))
            return Results.BadRequest(new { message = "Seleccione una prioridad válida." });

        if (request.Payload.ValueKind != JsonValueKind.Object
            || !request.Payload.TryGetProperty("form", out var form)
            || form.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new { message = "Los datos del formulario de la solicitud son obligatorios." });

        if (string.IsNullOrWhiteSpace(request.ClientName)
            || string.IsNullOrWhiteSpace(request.ExecutiveName)
            || string.IsNullOrWhiteSpace(request.ShipmentMode)
            || string.IsNullOrWhiteSpace(request.OriginName)
            || string.IsNullOrWhiteSpace(request.DestinationName))
            return Results.BadRequest(new { message = "Complete cliente, ejecutivo, modalidad y ruta." });

        entity.UpdateSubmission(
            priority,
            request.ClientName,
            request.ExecutiveName,
            request.ShipmentMode,
            request.OriginName,
            request.DestinationName,
            request.Payload.GetRawText());
        entity.SetRoute(request.PoeId, request.PoeName, request.PodId, request.PodName);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(entity.Id);
    }

    private sealed record UpdateSellerRateRequest(
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
        JsonElement Payload);

    // Resumen de solicitudes para la vista por roles, con autorización en el servidor.
    private static async Task<IResult> GetAdminDashboardAsync(
        ServiceDbContext db,
        SellerVisibilityService visibility,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!SellerRateEndpoints.IsAdminViewer(httpContext.User, visibility))
            return Results.Forbid();

        var requests = await db.RateRequests
            .AsNoTracking()
            .OrderByDescending(request => request.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(requests.Select(ToDto).ToArray());
    }

    private static object ToDto(Dhole.Pricing.Domain.Rates.Entities.RateRequest request)
    {
        using var document = JsonDocument.Parse(request.PayloadJson);
        var root = document.RootElement;
        var context = GetObject(root, "requestContext");
        var form = GetObject(root, "form");

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
            request.OriginName,
            request.DestinationName,
            equipmentType = FirstString(context, "equipmentType") ?? FirstString(form, "equipmentType") ?? FirstString(form, "equipmentSize"),
            equipmentQuantity = FirstInt(context, "equipmentQuantity") ?? FirstInt(form, "equipmentQuantity") ?? 1,
            modality = FirstString(context, "modality") ?? FirstString(form, "modality") ?? request.ShipmentMode,
            incotermName = FirstString(context, "incotermName") ?? FirstString(form, "incotermName") ?? FirstString(form, "incotermCode"),
            payload = root.Clone(),
        };
    }

    private static JsonElement? GetObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                return property.Value;
            }
        }
        return null;
    }

    private static string? FirstString(JsonElement? element, string name)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.Value.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim()
                : property.Value.ToString().Trim();
        }
        return null;
    }

    private static int? FirstInt(JsonElement? element, string name)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.Value.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value)) return value;
            if (int.TryParse(property.Value.ToString(), out value)) return value;
        }
        return null;
    }
}
