using System.Text.Json;
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

        app.MapGet("/api/pricing/rate-requests/dashboard", GetAdminDashboardAsync)
            .WithTags("Seller dashboards")
            .RequireAuthorization();

        return app;
    }

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
