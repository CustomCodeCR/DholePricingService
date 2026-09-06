using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
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
