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
        // The job is immutable, but the linked rate may have been deleted,
        // reapproved, or advanced to a newer revision since it was queued.
        // Surface that state so an operator never sees an unexplained 404.
        var rateIds = failures.Select(x => x.RateId).Distinct().ToArray();
        var currentRates = await db.RateHeaders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => rateIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status, x.RevisionNumber, x.IdtraNumber, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return Results.Ok(new
        {
            totalShown = failures.Count,
            issues = failures.Select(x =>
            {
                currentRates.TryGetValue(x.RateId, out var rate);
                var retryBlockReason = rate is null
                    ? "La tarifa original no existe en Pricing."
                    : rate.IsDeleted
                        ? "La tarifa fue eliminada; no se puede enviar a Dynamics."
                        : rate.Status != RateStatus.AcceptedByClient
                            ? "La tarifa ya no está aceptada por el cliente."
                            : string.IsNullOrWhiteSpace(rate.IdtraNumber)
                                ? "La tarifa no tiene IDTRA asignado."
                                : rate.RevisionNumber != x.RevisionNumber
                                    ? "La incidencia pertenece a una revisión anterior; revisá la versión actual."
                                    : null;
                return new
                {
                    x.RateId, x.RevisionNumber, x.Status, x.Attempts,
                    x.LastError, error = Diagnostic(x.ResponseJson),
                    x.CreatedAtUtc, x.NextAttemptAtUtc,
                    currentRateRevision = rate?.RevisionNumber,
                    retryBlockReason,
                    retryableWithoutDuplicateRisk =
                        retryBlockReason is null && CanRetry(x.Status, x.ResponseJson)
                };
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
                    or "production_quote_writes_not_approved"
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
        // The opaque CRUD key embeds a public UUID. Returning ONLY this key ID
        // to a Dynamics administrator lets the UI approve grants for the exact
        // integration identity used by Pricing, without revealing the API key.
        string? configuredKeyId = null;
        if (http.User.Claims.Any(c =>
                c.Type is "scope" or "scp" or "scopes" &&
                c.Value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Contains("dynamics.security.admin", StringComparer.OrdinalIgnoreCase)))
        {
            var secret = configuration["DynamicsQuoteSync:ApiKey"];
            if (secret is { Length: 81 } && secret.StartsWith("dkw1_", StringComparison.Ordinal) &&
                secret[37] == '_' &&
                Guid.TryParseExact(secret.Substring(5, 32), "N", out var id))
                configuredKeyId = id.ToString("D");
        }
        return Results.Ok(new { ready = check.Ready, code = check.Code,
            message = check.Message, configuredKeyId, noPostExecuted = true });
    }

    private static async Task<IResult> RetryPreWriteAsync(
        Guid rateId, HttpContext http, ServiceDbContext db,
        IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var rate = await db.RateHeaders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == rateId)
            .Select(x => new { x.Status, x.RevisionNumber, x.IdtraNumber, x.IsDeleted })
            .SingleOrDefaultAsync(cancellationToken);
        if (rate is null)
            return Results.NotFound(new
            {
                code = "rate_not_found",
                message = "La tarifa no existe en la base de Pricing que atiende este reintento."
            });
        if (rate.IsDeleted)
            return Results.Conflict(new
            {
                code = "rate_deleted_cannot_retry",
                message = "La tarifa está eliminada y no se puede sincronizar con Dynamics."
            });
        if (rate.Status != RateStatus.AcceptedByClient ||
            string.IsNullOrWhiteSpace(rate.IdtraNumber))
            return Results.Conflict(new
            {
                code = "rate_no_longer_accepted",
                message = "La tarifa debe permanecer aceptada y tener IDTRA para reintentar."
            });

        var job = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rateId && x.RevisionNumber == rate.RevisionNumber)
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            var priorRevisions = await db.DynamicsQuoteSyncJobs.AsNoTracking()
                .Where(x => x.RateId == rateId)
                .OrderByDescending(x => x.RevisionNumber)
                .Select(x => x.RevisionNumber)
                .Take(10)
                .ToArrayAsync(cancellationToken);
            return priorRevisions.Length > 0
                ? Results.Conflict(new
                {
                    code = "sync_revision_changed",
                    message = "La incidencia corresponde a otra revisión. No se reintentará una oferta desactualizada.",
                    currentRevision = rate.RevisionNumber,
                    jobRevisions = priorRevisions
                })
                : Results.NotFound(new
                {
                    code = "sync_job_not_found",
                    message = "La tarifa existe, pero no tiene un trabajo de sincronización en Pricing."
                });
        }

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
            var body = DynamicsQuotePayloadBuilder.Build(job.PayloadJson, schema, configuration);
            // Validate each exact-name lookup using the same active
            // template and approved read grants as execute-with-total.
            // This POST is a READ-ONLY preview: no quote is created.
            using var lookupRequest = new HttpRequestMessage(HttpMethod.Post,
                "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta/validate-lookups")
            {
                Content = new StringContent(JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8, "application/json")
            };
            lookupRequest.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var lookupResult = await clients.CreateClient("DynamicsQuoteSync")
                .SendAsync(lookupRequest, cancellationToken);
            var lookupJson = await lookupResult.Content.ReadAsStringAsync(cancellationToken);
            if (!lookupResult.IsSuccessStatusCode)
            {
                var (providerCode, _) =
                    DynamicsQuoteFailureDiagnostics.ReadProviderError(lookupJson);
                // Only schema identifiers are shown. Never return actual
                // customer names, IDTRA values, or raw upstream bodies.
                string? searchInput = null;
                string? searchField = null;
                if (lookupJson.Length <= 8192)
                {
                    using var detail = JsonDocument.Parse(lookupJson);
                    static string? Field(JsonElement x, string name) =>
                        x.ValueKind == JsonValueKind.Object &&
                        x.TryGetProperty(name, out var value) &&
                        value.ValueKind == JsonValueKind.String &&
                        System.Text.RegularExpressions.Regex.IsMatch(
                            value.GetString() ?? "", @"^[A-Za-z][A-Za-z0-9_]{0,79}$")
                            ? value.GetString() : null;
                    searchInput = Field(detail.RootElement, "lookupInput");
                    searchField = Field(detail.RootElement, "lookupSearchField");
                }
                return Results.Ok(new
                {
                    valid = false, stage = "lookup",
                    code = providerCode ?? "DYNAMICS_LOOKUP_VALIDATION_FAILED",
                    httpStatus = (int)lookupResult.StatusCode,
                    lookupInput = searchInput, lookupSearchField = searchField,
                    message = providerCode == "lookup_name_not_found"
                        ? "No existe un registro relacionado con el valor exacto enviado por Dhole. Revisá el campo de búsqueda indicado y el valor correspondiente en Dynamics."
                        : providerCode == "lookup_name_ambiguous"
                        ? "La búsqueda devolvió varios registros. Debe existir un único registro coincidente."
                        : providerCode is "client_denied" or "lookup_target_denied"
                        ? "La clave de integración de Pricing no tiene autorizada la lectura de una entidad o campo relacionado (Cuenta, IDTRA o divisa). Usá «Autorizar vínculos y venta total» con la misma clave activa configurada en Pricing. Esta comprobación no crea QUO."
                        : "No se pudieron comprobar los vínculos de la oferta en Dynamics.",
                    noPostExecuted = true
                });
            }
            using var previewResult = JsonDocument.Parse(lookupJson);
            var customerOmitted = previewResult.RootElement.ValueKind == JsonValueKind.Object &&
                previewResult.RootElement.TryGetProperty("customerOmitted", out var omission) &&
                omission.ValueKind == JsonValueKind.True;
            return Results.Ok(new
            {
                valid = true, stage = "lookup",
                code = customerOmitted
                    ? "DYNAMICS_LOOKUPS_VALID_CUSTOMER_OMITTED"
                    : "DYNAMICS_LOOKUPS_VALID",
                schemaVersion = schema.Version,
                customerOmitted,
                message = customerOmitted
                    ? "Cliente no encontrado en Dynamics. El conector omitirá customerid; Dataverse podría exigir un cliente. No se creó ninguna QUO."
                    : "El mapeo y los registros relacionados existen. No se creó ninguna QUO.",
                noPostExecuted = true
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
