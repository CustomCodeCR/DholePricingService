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
        group.MapGet("/dynamics-sync/issues", GetIssuesAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);
        group.MapPost("/{rateId:guid}/dynamics-sync/retry", RetryPreWriteAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);
        group.MapGet("/{rateId:guid}/dynamics-sync/validate", ValidateAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);
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
        ServiceDbContext db, CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> RetryPreWriteAsync(
        Guid rateId, ServiceDbContext db, CancellationToken cancellationToken)
    {
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
        Guid rateId, ServiceDbContext db, IHttpClientFactory clients,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
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
            _ = DynamicsQuotePayloadBuilder.Build(job.PayloadJson, schema);
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
                stage.GetString() is "schema" or "mapping";
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
