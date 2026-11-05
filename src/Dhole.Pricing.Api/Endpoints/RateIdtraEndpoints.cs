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
using Microsoft.Extensions.DependencyInjection;

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

        string normalizedRequested;
        try
        {
            var identifiers = IdtraNumberNormalizer.Parse(idtra);
            if (identifiers.Length is < 1 or > 30)
                throw new InvalidOperationException("Indique entre 1 y 30 IDTRA.");
            normalizedRequested = string.Join(", ", identifiers);
            if (normalizedRequested.Length > 2000)
                throw new InvalidOperationException("La lista IDTRA supera los 2000 caracteres.");
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.InvalidIdtraNumber",
                message = exception.Message
            });
        }

        // Npgsql/EF Core may enable retry-on-failure. The entire manually
        // opened transaction must execute INSIDE its execution strategy,
        // otherwise SaveChanges throws on a user-initiated transaction.
        var logger = loggerFactory.CreateLogger("Dhole.Pricing.RateIdtra");
        var strategy = db.Database.CreateExecutionStrategy();
        var stage = "begin-transaction";
        try
        {
            var (response, persisted) = await strategy.ExecuteAsync(async () =>
            {
                // Every execution-strategy attempt gets a fresh scope/DbContext.
                // A rolled-back attempt must not retain tracked IDTRA or outbox events.
                await using var attemptScope = http.RequestServices
                    .GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                var attemptDb = attemptScope.ServiceProvider.GetRequiredService<ServiceDbContext>();
                var attemptRateHeaders = attemptScope.ServiceProvider.GetRequiredService<IRateHeaderRepository>();
                var attemptAudit = attemptScope.ServiceProvider.GetRequiredService<IPricingAuditService>();

                stage = "begin-transaction";
                await using var transaction = await attemptDb.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted, cancellationToken);

                // Lock the quote while checking acceptance and existing references.
                // Concurrent users must not enqueue two CRM jobs for the same QUO.
                stage = "lock-rate";
                await using (var command = attemptDb.Database.GetDbConnection().CreateCommand())
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

                stage = "load-rate";
                var rate = await attemptRateHeaders.GetByIdWithDetailsAsync(rateId, cancellationToken);
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
                    string normalizedPrevious;
                    try
                    {
                        normalizedPrevious = NormalizeCsv(rate.IdtraNumber);
                    }
                    catch (InvalidOperationException)
                    {
                        return (Response: (IResult)Results.Conflict(new
                        {
                            code = "Pricing.IdtraAlreadyAssigned",
                            message = "Esta cotización ya tiene un IDTRA que debe revisarse antes de realizar cambios."
                        }), Persisted: false);
                    }
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

                stage = "set-idtra";
                var before = PricingAuditSnapshots.From(rate);
                try
                {
                    rate.SetIdtraNumber(normalizedRequested, http.GetCurrentUserId());
                    rate.SetOtherReferences(request.OtherReferences, http.GetCurrentUserId());
                }
                catch (InvalidOperationException exception)
                {
                    return (Response: (IResult)Results.BadRequest(new
                    {
                        code = "Pricing.InvalidIdtraNumber", message = exception.Message
                    }), Persisted: false);
                }

                stage = "audit-and-sync-job";
                await attemptAudit.PublishAsync(new PricingAuditEvent(
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

                stage = "save";
                await attemptDb.SaveChangesAsync(cancellationToken);
                stage = "commit";
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
                    // COMMIT succeeded. Redis problems cannot invalidate the IDTRA.
                    await cache.RemoveRateHeaderCacheAsync(rateId, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception,
                        "IDTRA already committed for rate {RateId}; cache invalidation failed. TraceId={TraceId}",
                        rateId, http.TraceIdentifier);
                }
            }

            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception,
                "IDTRA assignment failed. RateId={RateId} Stage={Stage} TraceId={TraceId}",
                rateId, stage, http.TraceIdentifier);
            return Results.Problem(
                title: "No se pudo guardar el IDTRA.",
                detail: "No se confirmó el guardado del IDTRA. Verifique su estado antes de intentar nuevamente.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "Pricing.IdtraAssignmentFailed",
                    ["stage"] = stage,
                    ["traceId"] = http.TraceIdentifier
                });
        }
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
