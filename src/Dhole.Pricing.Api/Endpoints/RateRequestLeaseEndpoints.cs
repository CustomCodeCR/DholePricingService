using System.Security.Claims;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateRequestLeaseEndpoints
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);
    public const string LeaseHeader = "X-Rate-Request-Lease";

    public static IEndpointRouteBuilder MapRateRequestLeaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rate-requests/{id:guid}")
            .WithTags("Rate requests")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/claim", ClaimAsync);
        group.MapPost("/heartbeat", HeartbeatAsync);
        group.MapPost("/release", ReleaseAsync);
        return app;
    }

    private static async Task<IResult> ClaimAsync(
        Guid id, LeaseRequest request, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null) return Results.Unauthorized();
        var now = DateTime.UtcNow;
        var expires = now.Add(Duration);
        var token = request.Token is { } current && current != Guid.Empty ? current : Guid.NewGuid();
        var name = context.User.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User.FindFirst("name")?.Value
            ?? context.User.FindFirst("preferred_username")?.Value
            ?? context.User.Identity?.Name ?? actor.Value.ToString();
        name = name.Trim();
        if (name.Length > 200) name = name[..200];

        // The conditional UPDATE arbitrates simultaneous claims across all API instances.
        // Normal reentry uses the tab token. Explicit recovery rotates an abandoned
        // lease only when its owner is the same authenticated user.
        var updated = await db.RateRequests
            .Where(x => x.Id == id && x.Status == RateRequestStatus.Open
                && (x.LockExpiresAtUtc == null || x.LockExpiresAtUtc <= now
                    || (x.LockedByUserId == actor.Value
                        && (x.LockToken == token || request.RecoverOwnLease))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockedByUserId, actor.Value)
                .SetProperty(x => x.LockedByName, name)
                .SetProperty(x => x.LockToken, token)
                .SetProperty(x => x.LockedAtUtc, now)
                .SetProperty(x => x.LockExpiresAtUtc, expires), ct);
        if (updated == 1) return Results.Ok(new { token, ownerName = name, expiresAtUtc = expires });
        return await ConflictOrMissingAsync(id, db, ct);
    }

    private static async Task<IResult> HeartbeatAsync(
        Guid id, LeaseRequest request, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null) return Results.Unauthorized();
        if (request.Token is null || request.Token == Guid.Empty)
            return Results.BadRequest(new { message = "Token de reserva requerido." });
        var now = DateTime.UtcNow;
        var expires = now.Add(Duration);
        var updated = await db.RateRequests
            .Where(x => x.Id == id && x.Status == RateRequestStatus.Open
                && x.LockedByUserId == actor.Value && x.LockToken == request.Token
                && x.LockExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LockExpiresAtUtc, expires), ct);
        return updated == 1 ? Results.Ok(new { expiresAtUtc = expires })
            : await ConflictOrMissingAsync(id, db, ct);
    }

    private static async Task<IResult> ReleaseAsync(
        Guid id, LeaseRequest request, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null) return Results.Unauthorized();
        if (request.Token is null || request.Token == Guid.Empty)
            return Results.BadRequest(new { message = "Token de reserva requerido." });
        // A stale tab must not release the lease taken by another user.
        await db.RateRequests
            .Where(x => x.Id == id && x.LockedByUserId == actor.Value && x.LockToken == request.Token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockedByUserId, (Guid?)null)
                .SetProperty(x => x.LockedByName, (string?)null)
                .SetProperty(x => x.LockToken, (Guid?)null)
                .SetProperty(x => x.LockedAtUtc, (DateTime?)null)
                .SetProperty(x => x.LockExpiresAtUtc, (DateTime?)null), ct);
        return Results.NoContent();
    }

    public static bool HasActiveLease(HttpContext context, RateRequest request)
        => context.GetCurrentUserId() is { } actor
            && Guid.TryParse(context.Request.Headers[LeaseHeader].ToString(), out var token)
            && request.LockedByUserId == actor
            && request.LockToken == token
            && request.LockExpiresAtUtc > DateTime.UtcNow;

    public static IResult LeaseRequired() => Results.Conflict(new {
        code = "Pricing.RateRequestLeaseRequired",
        message = "No tiene una reserva vigente de esta solicitud. Vuelva al dashboard y tome la solicitud."
    });

    private static async Task<IResult> ConflictOrMissingAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        var current = await db.RateRequests.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Status, x.LockedByName, x.LockExpiresAtUtc })
            .FirstOrDefaultAsync(ct);
        if (current is null) return Results.NotFound();
        var locked = current.LockExpiresAtUtc > DateTime.UtcNow;
        return Results.Conflict(new {
            code = "Pricing.RateRequestAlreadyClaimed",
            message = current.Status != RateRequestStatus.Open
                ? "La solicitud ya no está abierta."
                : locked ? $"La solicitud está siendo atendida por {current.LockedByName ?? "otro usuario"}."
                : "La solicitud está siendo tomada por otro usuario. Intente nuevamente.",
            ownerName = locked ? current.LockedByName : null,
            expiresAtUtc = locked ? current.LockExpiresAtUtc : null
        });
    }
    // A deliberate resume can reclaim ONLY the authenticated user's own lease.
    // Its token is rotated, invalidating the abandoned tab; other users stay locked.
    private sealed record LeaseRequest(Guid? Token, bool RecoverOwnLease = false);
}
