using System.Security.Claims;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestEndpoints
{
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
                    && x.Status is RateStatus.Sent
                        or RateStatus.AcceptedByClient
                        or RateStatus.RejectedByClient
                        or RateStatus.Closed)
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
