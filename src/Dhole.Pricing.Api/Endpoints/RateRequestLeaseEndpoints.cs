using System.Data;
using System.Security.Claims;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
        group.MapPost("/release-own", ReleaseOwnAsync);
        group.MapPost("/release-stale", ReleaseStaleAsync);
        return app;
    }

    private static async Task<IResult> ClaimAsync(
        Guid id, LeaseRequest request, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        try
        {
            return await ClaimCoreAsync(id, request, db, context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("PricingRateRequestLeaseAudit")
                .LogError(failure,
                    "Claim of request {RequestId} failed before opening the wizard; TraceId={TraceId}",
                    id, context.TraceIdentifier);
            return Results.Json(new
            {
                code = "Pricing.RateRequestLeaseUnavailable",
                message = "No se pudo reservar la solicitud en Pricing. Actualice la bandeja o reintente.",
                traceId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ClaimCoreAsync(
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

        // Enforce one active work lease per authenticated operator, across API nodes.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await TryLockOperatorAsync(db, actor.Value, ct))
            return Results.Conflict(new
            {
                code = "Pricing.RateRequestOperatorBusy",
                message = "Otra operación de reserva de este usuario está en curso. Reintente."
            });
        var updated = await db.RateRequests
            .Where(RateRequestLeaseClaimPolicy.Eligible(
                id, actor.Value, request.Token, request.RecoverOwnLease, now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockedByUserId, actor.Value)
                .SetProperty(x => x.LockedByName, name)
                .SetProperty(x => x.LockToken, token)
                .SetProperty(x => x.LockedAtUtc, now)
                .SetProperty(x => x.LockExpiresAtUtc, expires)
                .SetProperty(x => x.WorkStatus, x => x.WorkStatus ?? "Working")
                .SetProperty(x => x.WorkStatusUpdatedAtUtc, x => x.WorkStatus == null ? now : x.WorkStatusUpdatedAtUtc), ct);
        if (updated == 1)
        {
            var revoked = await RevokeOlderOwnedLeasesAsync(id, actor.Value, db, ct);
            await tx.CommitAsync(ct);
            if (revoked > 0)
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("PricingRateRequestLeaseAudit")
                    .LogInformation("Actor {ActorId} superseded {RevokedCount} old leases while claiming {RequestId}", actor.Value, revoked, id);
            return Results.Ok(new { token, ownerName = name, expiresAtUtc = expires });
        }
        await tx.RollbackAsync(ct);
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await TryLockOperatorAsync(db, actor.Value, ct))
            return Results.Conflict(new
            {
                code = "Pricing.RateRequestOperatorBusy",
                message = "Otra operación de reserva de este usuario está en curso. Reintente."
            });
        var updated = await db.RateRequests
            .Where(x => x.Id == id && x.Status == RateRequestStatus.Open
                && x.LockedByUserId == actor.Value && x.LockToken == request.Token
                && x.LockExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LockExpiresAtUtc, expires), ct);
        if (updated == 1)
        {
            var revoked = await RevokeOlderOwnedLeasesAsync(id, actor.Value, db, ct);
            await tx.CommitAsync(ct);
            if (revoked > 0)
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("PricingRateRequestLeaseAudit")
                    .LogInformation("Actor {ActorId} superseded {RevokedCount} old leases while renewing {RequestId}", actor.Value, revoked, id);
            return Results.Ok(new { expiresAtUtc = expires });
        }
        await tx.RollbackAsync(ct);
        return await ConflictOrMissingAsync(id, db, ct);
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

    // Never claim again in order to release a reservation. A different browser tab
    // or impersonation session may have lost its local token.
    private static async Task<IResult> ReleaseOwnAsync(
        Guid id, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null) return Results.Unauthorized();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await TryLockOperatorAsync(db, actor.Value, ct))
            return Results.Conflict(new
            {
                code = "Pricing.RateRequestOperatorBusy",
                message = "Otra operación de reserva de este usuario está en curso. Reintente."
            });
        var released = await ClearLeaseAsync(
            db.RateRequests.Where(x => x.Id == id
                && x.Status == RateRequestStatus.Open
                && x.LockedByUserId == actor.Value
                && x.LockToken != null), ct);
        await tx.CommitAsync(ct);
        if (released == 0)
            return Results.Conflict(new { code = "Pricing.RateRequestNotOwned",
                message = "La solicitud no tiene una reserva activa a su nombre. Actualice la bandeja." });
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PricingRateRequestLeaseAudit")
            .LogInformation("Actor {ActorId} explicitly released own lease for request {RequestId}",
                actor.Value, id);
        return Results.NoContent();
    }

    // An operator may recover abandoned work, but NEVER displace a lease that
    // is receiving heartbeats. The 45-second threshold is based on the server's
    // last renewal (lease expiry minus the fixed two-minute duration).
    private static async Task<IResult> ReleaseStaleAsync(
        Guid id, ServiceDbContext db, HttpContext context, CancellationToken ct)
    {
        var actor = context.GetCurrentUserId();
        if (actor is null) return Results.Unauthorized();
        var now = DateTime.UtcNow;
        var released = await ClearLeaseAsync(
            db.RateRequests.Where(RateRequestLeaseClaimPolicy.StaleLease(id, now)), ct);
        if (released == 0)
            return Results.Conflict(new { code = "Pricing.RateRequestLeaseStillActive",
                message = "La reserva sigue activa o ya fue liberada. Actualice la bandeja." });
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PricingRateRequestLeaseAudit")
            .LogWarning("Actor {ActorId} released inactive lease for request {RequestId}, inactivity >= 45 seconds",
                actor.Value, id);
        return Results.NoContent();
    }

    private static Task<int> ClearLeaseAsync(IQueryable<RateRequest> query, CancellationToken ct)
        => query.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.LockedByUserId, (Guid?)null)
            .SetProperty(x => x.LockedByName, (string?)null)
            .SetProperty(x => x.LockToken, (Guid?)null)
            .SetProperty(x => x.LockedAtUtc, (DateTime?)null)
            .SetProperty(x => x.LockExpiresAtUtc, (DateTime?)null), ct);

    // Query a PostgreSQL advisory lock using ADO.NET's scalar API. EF Core's
    // ExecuteSql* methods are designed for non-query SQL and do not validate
    // the result of SELECT pg_advisory_xact_lock(...). A try-lock gives a
    // deterministic 409 instead of an opaque timeout/500 under contention.
    private static async Task<bool> TryLockOperatorAsync(
        ServiceDbContext db, Guid actor, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Lease acquisition requires a database transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_try_advisory_xact_lock(hashtextextended(@actor, 0::bigint))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "actor";
        parameter.DbType = DbType.String;
        parameter.Value = actor.ToString("N");
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static Task<int> RevokeOlderOwnedLeasesAsync(
        Guid activeId, Guid actor, ServiceDbContext db, CancellationToken ct)
        => db.RateRequests
            .Where(RateRequestLeaseClaimPolicy.OtherOwnedLeases(actor, activeId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LockedByUserId, (Guid?)null)
                .SetProperty(x => x.LockedByName, (string?)null)
                .SetProperty(x => x.LockToken, (Guid?)null)
                .SetProperty(x => x.LockedAtUtc, (DateTime?)null)
                .SetProperty(x => x.LockExpiresAtUtc, (DateTime?)null), ct);

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
