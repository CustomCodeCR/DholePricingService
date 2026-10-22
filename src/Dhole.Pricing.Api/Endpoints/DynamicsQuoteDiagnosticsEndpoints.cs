using System.Security.Claims;
using System.Data;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using System.Net;
using System.Text.Json;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// One authenticated URL per rate to diagnose Dynamics synchronization without
/// SSH, database access or API credentials. Supervisors can inspect failures
/// and explicitly retry failures proven to have occurred before the POST.
/// </summary>
public static class DynamicsQuoteDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDynamicsQuoteDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates").WithTags("Dynamics quote synchronization")
            .RequireAuthorization();

        group.MapGet("/{rateId:guid}/dynamics-sync", GetOneAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        // Dynamics administrators inspect integration errors from the Dynamics
        // operations console. The handler verifies either Dynamics admin scope
        // or the existing Pricing margin-approval scope.
        group.MapGet("/dynamics-sync/issues", GetIssuesAsync);
        group.MapGet("/dynamics-sync/write-readiness", GetReadinessAsync);
        group.MapPost("/{rateId:guid}/dynamics-sync/retry", RetryPreWriteAsync);
        // Explicitly queue an already accepted rate with IDTRA if it predated
        // the integration or the acceptance event was not recorded. Does not
        // perform a Dataverse write in the HTTP request.
        group.MapPost("/{rateId:guid}/dynamics-sync/enqueue", EnqueueAcceptedAsync);
        group.MapGet("/{rateId:guid}/dynamics-sync/validate", ValidateAsync);
        return app;
    }

    private static async Task<IResult> GetOneAsync(
        Guid rateId, ServiceDbContext db, CancellationToken cancellationToken)
    {
        var rate = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new { x.Status, x.IdtraNumber, x.RevisionNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (rate is null) return Results.NotFound(new { code = "rate_not_found" });

        var jobs = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            rateId,
            rateStatus = rate.Status.ToString(),
            hasIdtra = !string.IsNullOrWhiteSpace(rate.IdtraNumber),
            currentRevision = rate.RevisionNumber,
            status = jobs.FirstOrDefault()?.Status ?? "NotQueued",
            lastError = jobs.FirstOrDefault()?.LastError,
            deliveries = jobs.Select(x => new
            {
                x.RevisionNumber, x.Status, x.Attempts, x.LastError,
                error = Diagnostic(x.ResponseJson),
                x.CreatedAtUtc, x.NextAttemptAtUtc, x.CompletedAtUtc,
                retryableWithoutDuplicateRisk = CanRetry(x.Status, x.ResponseJson)
            })
        });
    }

    private static async Task<IResult> GetIssuesAsync(
        HttpContext http, ServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var failures = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.Status == "NeedsAttention" || x.Status == "Retry")
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            totalShown = failures.Count,
            issues = failures.Select(x => new
            {
                x.RateId, x.RevisionNumber, x.Status, x.Attempts,
                x.LastError, error = Diagnostic(x.ResponseJson),
                x.CreatedAtUtc, x.NextAttemptAtUtc,
                retryableWithoutDuplicateRisk = CanRetry(x.Status, x.ResponseJson)
            })
        });
    }


    private static async Task<IResult> EnqueueAcceptedAsync(
        Guid rateId, HttpContext http, ServiceDbContext db,
        IRateHeaderRepository rateHeaders, IPricingAuditService audit,
        CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        // Lock the rate so a concurrent IDTRA assignment or approval cannot
        // create a second synchronization job for the same revision.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using (var lockCommand = db.Database.GetDbConnection().CreateCommand())
        {
            lockCommand.Transaction = transaction.GetDbTransaction();
            lockCommand.CommandText =
                """SELECT id FROM pricing."RateHeaders" WHERE id = @id FOR UPDATE""";
            var parameter = lockCommand.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = rateId;
            lockCommand.Parameters.Add(parameter);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not Guid)
                return Results.NotFound(new { code = "rate_not_found" });
        }

        var rate = await rateHeaders.GetByIdWithDetailsAsync(rateId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Results.NotFound(new { code = "rate_not_found" });
        if (rate.Status != RateStatus.AcceptedByClient ||
            string.IsNullOrWhiteSpace(rate.IdtraNumber))
            return Results.Conflict(new
            {
                code = "rate_not_ready_for_dynamics",
                message = "La tarifa debe estar aceptada y tener IDTRA antes de crear la oferta."
            });
        if (rate.RevisionNumber < 1)
            return Results.Conflict(new { code = "invalid_rate_revision" });

        var existing = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rate.Id &&
                        x.RevisionNumber == rate.RevisionNumber)
            .Select(x => new { x.Status, x.Attempts })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return Results.Ok(new
            {
                rateId, revisionNumber = rate.RevisionNumber,
                status = existing.Status, alreadyQueued = true,
                message = "Ya existe una sincronización para esta revisión. No se duplicó."
            });

        var eventId = Guid.NewGuid();
        var snapshot = PricingAuditSnapshots.From(rate);
        db.DynamicsQuoteSyncJobs.Add(new Dhole.Pricing.Persistence.Auditing.DynamicsQuoteSyncJob
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            RateId = rate.Id,
            RevisionNumber = rate.RevisionNumber,
            PayloadJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = "Pending",
            NextAttemptAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await audit.PublishAsync(new PricingAuditEvent(
            EventType: PricingAuditEventTypes.RateHeaderUpdated,
            Action: PricingAuditActions.Updated,
            EntityType: PricingAuditEntityTypes.RateHeader,
            EntityId: rate.Id,
            ActorUserId: http.GetCurrentUserId(),
            Before: snapshot,
            After: snapshot,
            Payload: new
            {
                rate.Id, rate.RevisionNumber,
                Source = "ManualDynamicsQuoteSyncEnqueue",
                DynamicsSyncQueued = true
            }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Accepted($"/api/pricing/rates/{rateId}/dynamics-sync",
            new
            {
                rateId, revisionNumber = rate.RevisionNumber,
                status = "Pending", alreadyQueued = false,
                message = "Sincronización encolada. Ninguna escritura en Dynamics se realizó desde este endpoint."
            });
    }


    private sealed record QuoteReadiness(bool Ready, string Code, string Message);

    private static async Task<QuoteReadiness> CheckQuoteReadinessAsync(
        IHttpClientFactory clients, IConfiguration configuration, CancellationToken ct)
    {
        var apiKey = configuration["DynamicsQuoteSync:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new(false, "DYNAMICS_API_KEY_MISSING",
                "Falta configurar DynamicsQuoteSync__ApiKey en el servicio de Pricing.");

        const string url = "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta/write-readiness?connectionCode=principal";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var response = await clients.CreateClient("DynamicsQuoteSync")
                .SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var (code, title) = DynamicsQuoteFailureDiagnostics.ReadProviderError(body);
                return new(false, code ?? "DYNAMICS_PREFLIGHT_HTTP_" + (int)response.StatusCode,
                    title ?? "El conector no autorizó la comprobación de QUO.");
            }
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("ready", out var ready) ||
                ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(false, "DYNAMICS_PREFLIGHT_INVALID",
                    "El conector no devolvió una confirmación válida de escrituras.");
            var enabled = ready.GetBoolean();
            var codeText = root.TryGetProperty("code", out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            // Do not copy arbitrary upstream response text into the admin API.
            return new(enabled,
                codeText is "DYNAMICS_QUOTE_WRITES_READY" or "production_writes_not_approved"
                    ? codeText : "DYNAMICS_PREFLIGHT_UNKNOWN",
                enabled ? "La política del conector permite crear QUO."
                    : "Producción todavía rechaza las escrituras de QUO. Se requiere autorización del cambio.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(false, "DYNAMICS_PREFLIGHT_UNAVAILABLE",
                "No se pudo comprobar la autorización del conector. No se reintentó la tarifa.");
        }
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext http, IHttpClientFactory clients, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var check = await CheckQuoteReadinessAsync(clients, configuration, cancellationToken);
        return Results.Ok(new { ready = check.Ready, code = check.Code,
            message = check.Message, noPostExecuted = true });
    }

    private static async Task<IResult> RetryPreWriteAsync(
        Guid rateId, HttpContext http, ServiceDbContext db,
        IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var rate = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new { x.Status, x.RevisionNumber, x.IdtraNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (rate is null) return Results.NotFound(new { code = "rate_not_found" });

        if (rate.Status.ToString() != "AcceptedByClient" ||
            string.IsNullOrWhiteSpace(rate.IdtraNumber))
            return Results.Conflict(new { code = "rate_no_longer_accepted" });

        var job = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rateId && x.RevisionNumber == rate.RevisionNumber)
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null) return Results.NotFound(new { code = "sync_job_not_found" });

        // Never replay old/uncategorized or post-attempt failures. An HTTP
        // timeout or 409 might have successfully written to Dataverse.
        if (!CanRetry(job.Status, job.ResponseJson))
            return Results.Conflict(new
            {
                code = "sync_reconciliation_required",
                detail = "No se puede garantizar que Dynamics no haya procesado el POST anterior."
            });

        // A successful mapping preview does not imply Dataverse writes are
        // approved. Fail before changing the job so policy-denied retries do
        // not silently bounce back to NeedsAttention with another HTTP 403.
        var readiness = await CheckQuoteReadinessAsync(clients, configuration, cancellationToken);
        if (!readiness.Ready)
            return Results.Conflict(new
            {
                code = "dynamics_quote_writes_not_ready",
                providerCode = readiness.Code,
                message = readiness.Message,
                retryQueued = false
            });

        var changed = await db.DynamicsQuoteSyncJobs
            .Where(x => x.Id == job.Id && x.Status == "NeedsAttention")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Pending")
                .SetProperty(x => x.Attempts, 0)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.ResponseJson, (string?)null)
                .SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null),
                cancellationToken);
        return changed == 1
            ? Results.Accepted($"/api/pricing/rates/{rateId}/dynamics-sync",
                new { rateId, status = "Pending", detail = "Reintento encolado con el mismo identificador." })
            : Results.Conflict(new { code = "sync_status_changed" });
    }

    private static async Task<IResult> ValidateAsync(
        Guid rateId, HttpContext http, ServiceDbContext db, IHttpClientFactory clients,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var job = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null) return Results.NotFound(new { code = "sync_job_not_found" });

        var apiKey = configuration["DynamicsQuoteSync:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.Ok(new
            {
                valid = false, stage = "configuration",
                code = "DYNAMICS_API_KEY_MISSING",
                message = "La clave API no está configurada en Pricing."
            });

        const string url = "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta/schema?connectionCode=principal";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var response = await clients.CreateClient("DynamicsQuoteSync")
                .SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (providerCode, providerTitle) =
                    DynamicsQuoteFailureDiagnostics.ReadProviderError(text);
                return Results.Ok(new
                {
                    valid = false, stage = "schema", code = providerCode ?? "DYNAMICS_SCHEMA_HTTP_ERROR",
                    httpStatus = (int)response.StatusCode,
                    message = providerTitle ?? "No fue posible consultar el esquema activo."
                });
            }
            var schema = DynamicsQuotePayloadBuilder.ReadSchema(text);
            _ = DynamicsQuotePayloadBuilder.Build(job.PayloadJson, schema, configuration);
            return Results.Ok(new
            {
                valid = true, stage = "mapping", code = "DYNAMICS_MAPPING_VALID",
                schemaVersion = schema.Version,
                message = "La plantilla activa acepta el mapeo de esta tarifa. No se ejecutó ningún POST."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var (error, diagnostic) = DynamicsQuoteFailureDiagnostics.Format(
                "mapping", ex, false);
            return Results.Ok(new
            {
                valid = false, stage = "mapping",
                code = JsonDocument.Parse(diagnostic).RootElement.GetProperty("code").GetString(),
                message = error,
                noPostExecuted = true
            });
        }
    }

    private static bool CanInspectQuoteSync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        var scopes = user.Claims
            .Where(claim => claim.Type.Equals("scope", StringComparison.OrdinalIgnoreCase) ||
                            claim.Type.Equals("scp", StringComparison.OrdinalIgnoreCase) ||
                            claim.Type.Equals("scopes", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return scopes.Contains(PricingConstants.Scopes.RateApproveLowMargin) ||
               scopes.Contains("dynamics.admin") ||
               scopes.Contains("dynamics.security.admin") ||
               scopes.Contains("dynamics.*") ||
               scopes.Contains("pricing.*") ||
               scopes.Contains("*");
    }

    private static bool CanRetry(string status, string? diagnosticJson)
    {
        if (status != "NeedsAttention") return false;
        try
        {
            using var doc = JsonDocument.Parse(diagnosticJson ?? "{}");
            var root = doc.RootElement;
            return root.TryGetProperty("postAttempted", out var posted) &&
                posted.ValueKind == JsonValueKind.False &&
                root.TryGetProperty("stage", out var stage) &&
                stage.ValueKind == JsonValueKind.String &&
                (stage.GetString() is "schema" or "mapping" ||
                 stage.GetString() == "execute" &&
                 root.TryGetProperty("providerCode", out var provider) &&
                 DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(
                     provider.GetString()));
        }
        catch (JsonException) { return false; }
    }

    private static JsonElement? Diagnostic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("code", out _) ? root.Clone() : null;
        }
        catch (JsonException) { return null; }
    }
}
