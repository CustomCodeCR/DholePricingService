using System.Data;
using System.Security.Claims;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// The IDTRA is an operational identifier, not a commercial tariff modification.
/// Only accepted, IDTRA-less quotes may receive it; the audit/outbox and CRM
/// delivery job are persisted in the same transaction.
/// </summary>
public static class RateIdtraEndpoints
{
    public static IEndpointRouteBuilder MapRateIdtraEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/pricing/rates/{rateId:guid}/idtra", AssignAsync)
            .WithTags("Rates")
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> AssignAsync(
        Guid rateId,
        AssignIdtraRequest request,
        ServiceDbContext db,
        IRateHeaderRepository rateHeaders,
        IPricingAuditService audit,
        IRateHeaderCacheService cache,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!HasScope(http.User, PricingConstants.Scopes.RateUpdate) &&
            !HasScope(http.User, PricingConstants.Scopes.RateApproveLowMargin))
            return Results.Forbid();

        var idtra = request.IdtraNumber?.Trim();
        if (string.IsNullOrWhiteSpace(idtra) || idtra.Length > 100)
            return Results.BadRequest(new
            {
                code = "Pricing.InvalidIdtraNumber",
                message = "Ingresá un IDTRA de 1 a 100 caracteres."
            });

        // Serialize concurrent assignments on the rate row before reading it;
        // two simultaneous requests must not create two sync jobs or overwrite
        // one another's IDTRA.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            cmd.Transaction = transaction.GetDbTransaction();
            cmd.CommandText = "SELECT id FROM pricing.\"RateHeaders\" WHERE id = @id FOR UPDATE";
            var idParameter = cmd.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = rateId;
            cmd.Parameters.Add(idParameter);
            var locked = await cmd.ExecuteScalarAsync(cancellationToken);
            if (locked is null || locked == DBNull.Value)
                return Results.NotFound(new { code = "Pricing.RateHeaderNotFound" });
        }

        var rate = await rateHeaders.GetByIdWithDetailsAsync(rateId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Results.NotFound(new { code = "Pricing.RateHeaderNotFound" });

        if (rate.Status != RateStatus.AcceptedByClient)
            return Results.Conflict(new
            {
                code = "Pricing.IdtraRequiresClientAcceptance",
                message = "El IDTRA solo puede asignarse después de la aceptación del cliente."
            });

        if (!string.IsNullOrWhiteSpace(rate.IdtraNumber))
        {
            if (string.Equals(rate.IdtraNumber, idtra, StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new
                {
                    rateId, idtraNumber = rate.IdtraNumber,
                    status = "AcceptedByClient",
                    message = "El IDTRA ya estaba registrado."
                });

            return Results.Conflict(new
            {
                code = "Pricing.IdtraAlreadyAssigned",
                message = "Esta cotización ya tiene IDTRA. No puede sobrescribirse desde esta acción."
            });
        }

        var before = PricingAuditSnapshots.From(rate);
        try
        {
            rate.SetIdtraNumber(idtra, http.GetCurrentUserId());
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { code = "Pricing.InvalidIdtraNumber", message = exception.Message });
        }

        await audit.PublishAsync(new PricingAuditEvent(
            EventType: PricingAuditEventTypes.RateHeaderUpdated,
            Action: PricingAuditActions.Updated,
            EntityType: PricingAuditEntityTypes.RateHeader,
            EntityId: rate.Id,
            ActorUserId: http.GetCurrentUserId(),
            Before: before,
            After: PricingAuditSnapshots.From(rate),
            Payload: new
            {
                rate.Id, rate.IdtraNumber, rate.RevisionNumber,
                Source = "IdtraAssignedAfterClientAcceptance",
                DynamicsSyncQueued = true
            }), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cache.RemoveRateHeaderCacheAsync(rate.Id, cancellationToken);
        return Results.Ok(new
        {
            rateId, idtraNumber = rate.IdtraNumber,
            status = "AcceptedByClient",
            dynamicsSync = "Pending",
            message = "IDTRA guardado. La creación de la Quote en Dynamics quedó en cola."
        });
    }

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.Claims.Where(c => c.Type is "scope" or "scp" or "scopes")
            .SelectMany(c => c.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(value => value == scope || value is "pricing.*" or "*");

    public sealed record AssignIdtraRequest(string? IdtraNumber);
}
