using System.Data;
using System.Security.Claims;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Rates;
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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!HasScope(http.User, PricingConstants.Scopes.RateUpdate) &&
            !HasScope(http.User, PricingConstants.Scopes.RateApproveLowMargin))
            return Results.Forbid();

        var idtra = request.IdtraNumber?.Trim();
        if (string.IsNullOrWhiteSpace(idtra) || idtra.Length > 2000 ||
            request.OtherReferences?.Length > 2000)
            return Results.BadRequest(new
            {
                code = "Pricing.InvalidIdtraNumber",
                message = "Ingrese los IDTRA separados por comas (máximo 2000 caracteres) y otras referencias de hasta 2000 caracteres."
            });

        // Npgsql/EF Core may enable retry-on-failure. The entire manually
        // opened transaction must execute INSIDE its execution strategy,
        // otherwise SaveChanges throws on a user-initiated transaction.
        var logger = loggerFactory.CreateLogger("Dhole.Pricing.RateIdtra");
        var strategy = db.Database.CreateExecutionStrategy();
        var (response, persisted) = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

            // Lock the quote while checking acceptance and existing references.
            // Concurrent users must not enqueue two CRM jobs for the same QUO.
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "SELECT id FROM pricing.\"RateHeaders\" WHERE id = @id FOR UPDATE";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = rateId;
                command.Parameters.Add(parameter);
                var locked = await command.ExecuteScalarAsync(cancellationToken);
                if (locked is null || locked == DBNull.Value)
                    return (Response: (IResult)Results.NotFound(new
                    {
                        code = "Pricing.RateHeaderNotFound"
                    }), Persisted: false);
            }

            var rate = await rateHeaders.GetByIdWithDetailsAsync(rateId, cancellationToken);
            if (rate is null || rate.IsDeleted)
                return (Response: (IResult)Results.NotFound(new
                {
                    code = "Pricing.RateHeaderNotFound"
                }), Persisted: false);

            if (rate.Status != RateStatus.AcceptedByClient)
                return (Response: (IResult)Results.Conflict(new
                {
                    code = "Pricing.IdtraRequiresClientAcceptance",
                    message = "El IDTRA solo puede asignarse después de la aceptación del cliente."
                }), Persisted: false);

            if (!string.IsNullOrWhiteSpace(rate.IdtraNumber))
            {
                // A retry after a successful commit must return 200, not 409,
                // even when the caller uses a different CSV spacing.
                var normalizedPrevious = NormalizeCsv(rate.IdtraNumber);
                var normalizedRequested = NormalizeCsv(idtra!);
                if (string.Equals(normalizedPrevious, normalizedRequested, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rate.OtherReferences?.Trim() ?? "",
                        request.OtherReferences?.Trim() ?? "", StringComparison.Ordinal))
                    return (Response: (IResult)Results.Ok(new
                    {
                        rateId, idtraNumber = rate.IdtraNumber,
                        otherReferences = rate.OtherReferences,
                        status = "AcceptedByClient",
                        message = "Los IDTRA ya estaban registrados."
                    }), Persisted: false);

                return (Response: (IResult)Results.Conflict(new
                {
                    code = "Pricing.IdtraAlreadyAssigned",
                    message = "Esta cotización ya tiene IDTRA. No puede sobrescribirse desde esta acción."
                }), Persisted: false);
            }

            var before = PricingAuditSnapshots.From(rate);
            try
            {
                rate.SetIdtraNumber(idtra!, http.GetCurrentUserId());
                rate.SetOtherReferences(request.OtherReferences, http.GetCurrentUserId());
            }
            catch (InvalidOperationException exception)
            {
                return (Response: (IResult)Results.BadRequest(new
                {
                    code = "Pricing.InvalidIdtraNumber", message = exception.Message
                }), Persisted: false);
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
                    rate.Id, rate.IdtraNumber, rate.OtherReferences, rate.RevisionNumber,
                    Source = "IdtraAssignedAfterClientAcceptance",
                    DynamicsSyncQueued = true
                }), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (Response: (IResult)Results.Ok(new
            {
                rateId, idtraNumber = rate.IdtraNumber, otherReferences = rate.OtherReferences,
                status = "AcceptedByClient",
                dynamicsSync = "Pending",
                message = "IDTRA guardado. La creación de la Quote en Dynamics quedó en cola."
            }), Persisted: true);
        });

        if (persisted)
        {
            try
            {
                // Database COMMIT already succeeded. Redis must not turn an
                // actually saved IDTRA into a misleading HTTP 500 on the UI.
                await cache.RemoveRateHeaderCacheAsync(rateId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "IDTRA for rate {RateId} was committed, but cache invalidation failed. TraceId={TraceId}",
                    rateId, http.TraceIdentifier);
            }
        }

        return response;
    }

    private static string NormalizeCsv(string input) =>
        IdtraNumberNormalizer.NormalizeCsvOrNull(input) ?? "";

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.Claims.Where(c => c.Type is "scope" or "scp" or "scopes")
            .SelectMany(c => c.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(value => value == scope || value is "pricing.*" or "*");

    public sealed record AssignIdtraRequest(string? IdtraNumber, string? OtherReferences = null);
}
