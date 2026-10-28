using System.Net.Http.Json;
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
        // Exposes the complete operator queue, including pending/in-flight
        // jobs and recently confirmed success, not just errors.
        group.MapGet("/dynamics-sync/queue", GetIssuesAsync);
        group.MapGet("/dynamics-sync/write-readiness", GetReadinessAsync);
        group.MapPost("/{rateId:guid}/dynamics-sync/retry", RetryPreWriteAsync);
        // Manual recovery is different from retry: the former is allowed only
        // when the connector proves the previous header was rejected 4xx.
        group.MapPost("/{rateId:guid}/dynamics-sync/recover-rejected",
            RecoverRejectedAsync);
        // A previous 409 may be stale: reconcile only when BOTH writes are
        // confirmed succeeded in the connector's durable ledger. No POST.
        group.MapPost("/{rateId:guid}/dynamics-sync/reconcile-confirmed",
            ReconcileConfirmedAsync);
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
        // Nonterminal jobs remain visible throughout the entire lifecycle.
        // Previously Pending and Processing disappeared from the UI despite
        // Dataverse not having confirmed a successful write.
        var active = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.Status == "Pending" || x.Status == "Processing" ||
                        x.Status == "Retry" || x.Status == "NeedsAttention")
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(150)
            .ToListAsync(cancellationToken);
        var completed = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.Status == "Completed")
            .OrderByDescending(x => x.CompletedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);
        var jobs = active.Concat(completed)
            .OrderByDescending(x => x.CreatedAtUtc).ToList();
        var counts = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Total = x.Count() })
            .ToListAsync(cancellationToken);
        int Count(string state) => counts.FirstOrDefault(x => x.Status == state)?.Total ?? 0;
        // Accepted quotes with an IDTRA but NO job for the current revision
        // must not disappear from monitoring just because the acceptance
        // event was missed. Expose read-only, never auto-create or resend.
        var notQueued = await db.RateHeaders.AsNoTracking()
            .Where(r => !r.IsDeleted && r.Status == RateStatus.AcceptedByClient &&
                r.IdtraNumber != null && r.IdtraNumber != "" &&
                !db.DynamicsQuoteSyncJobs.Any(j =>
                    j.RateId == r.Id && j.RevisionNumber == r.RevisionNumber))
            .OrderByDescending(r => r.ValidFrom)
            .Take(30)
            .Select(r => new
            {
                rateId = r.Id,
                r.RateCode,
                r.RateName,
                r.ClientName,
                r.IdtraNumber,
                r.RevisionNumber,
                r.PolName,
                r.PoeName,
                r.PodName,
                r.IncotermName,
                r.TotalSaleAmount,
                r.TotalCostAmount,
                r.CurrencyCode,
                r.CurrencyName
            })
            .ToListAsync(cancellationToken);

        // The job is immutable, but the linked rate may have been deleted,
        // reapproved, or advanced to a newer revision since it was queued.
        // Surface that state so an operator never sees an unexplained 404.
        var rateIds = jobs.Select(x => x.RateId).Distinct().ToArray();
        var currentRates = await db.RateHeaders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => rateIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status, x.RevisionNumber, x.IdtraNumber, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return Results.Ok(new
        {
            totalShown = jobs.Count,
            missingQueuedJobs = notQueued,
            missingQueuedCountShown = notQueued.Count,
            refreshedAtUtc = DateTime.UtcNow,
            queue = new
            {
                pending = Count("Pending"),
                processing = Count("Processing"),
                retry = Count("Retry"),
                needsAttention = Count("NeedsAttention"),
                completed = Count("Completed")
            },
            issues = jobs.Select(x =>
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
                    rate = SummarizeAcceptedRate(x.PayloadJson),
                    crm = ExtractWrittenQuoteEvidence(x.ResponseJson, x.Status),
                    x.LastError, error = Diagnostic(x.ResponseJson),
                    x.CreatedAtUtc, x.NextAttemptAtUtc, x.CompletedAtUtc,
                    currentRateRevision = rate?.RevisionNumber,
                    retryBlockReason,
                    retryableWithoutDuplicateRisk =
                        retryBlockReason is null && CanRetry(x.Status, x.ResponseJson)
                };
            })
        });
    }


    // These fields come from the immutable accepted snapshot for THIS
    // revision; do not show newer edited commercial details on an old job.
    private static object SummarizeAcceptedRate(string snapshot)
    {
        try
        {
            using var json = JsonDocument.Parse(snapshot);
            var rate = json.RootElement;
            if (rate.ValueKind != JsonValueKind.Object)
                return new { rateCode = (string?)null, clientName = (string?)null,
                    rateName = (string?)null, executiveName = (string?)null,
                    idtraNumber = (string?)null, shipmentMode = (string?)null,
                    incotermName = (string?)null, polName = (string?)null,
                    poeName = (string?)null, podName = (string?)null,
                    totalSaleAmount = (decimal?)null, totalCostAmount = (decimal?)null,
                    totalUtilityAmount = (decimal?)null, currencyCode = (string?)null,
                    currencyName = (string?)null };
            string? Read(string name) =>
                rate.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            decimal? Number(string name) =>
                rate.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDecimal(out var number) ? number : null;
            return new
            {
                rateCode = Read("rateCode") ?? Read("quoNumber"),
                rateName = Read("rateName"),
                clientName = Read("clientName"),
                executiveName = Read("executiveName"),
                idtraNumber = Read("idtraNumber"),
                shipmentMode = Read("shipmentMode"),
                incotermName = Read("incotermName"),
                polName = Read("polName"),
                poeName = Read("poeName"),
                podName = Read("podName"),
                currencyCode = Read("currencyCode"),
                currencyName = Read("currencyName"),
                totalSaleAmount = Number("totalSaleAmount"),
                totalCostAmount = Number("totalCostAmount"),
                totalUtilityAmount = Number("totalUtilityAmount")
            };
        }
        catch (JsonException)
        {
            return new { error = "accepted_snapshot_invalid" };
        }
    }

    // A Completed job records connector success, NOT proof that the quote
    // and its sales line can still be read from live Dataverse. Never claim
    // live confirmation without the separate read-only verification.
    private static object ExtractWrittenQuoteEvidence(string? json, string jobStatus)
    {
        Guid? quoteId = null;
        Guid? lineId = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var parsed = JsonDocument.Parse(json);
                var root = parsed.RootElement;
                Guid? Read(string name) =>
                    root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty(name, out var raw) &&
                    raw.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(raw.GetString(), out var value) &&
                    value != Guid.Empty ? value : null;
                quoteId = Read("recordId");
                lineId = Read("lineRecordId");
            }
            catch (JsonException) { /* Legacy diagnostics are not write proof. */ }
        }
        return new
        {
            quoteId,
            lineId,
            connectorRecordedSuccess =
                jobStatus == "Completed" && quoteId.HasValue && lineId.HasValue,
            liveDataverseVerified = false,
            verification = quoteId.HasValue
                ? "pending_live_verification" : "no_record_identifiers"
        };
    }

    private static async Task<IResult> EnqueueAcceptedAsync(
        Guid rateId, HttpContext http, ServiceDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!CanInspectQuoteSync(http.User)) return Results.Forbid();
        var logger = loggerFactory.CreateLogger(nameof(DynamicsQuoteDiagnosticsEndpoints));
        // All writes in this endpoint are to the Pricing queue. It never POSTs
        // to Dynamics. Serialize against acceptance/revision updates by
        // locking the physical rate row inside a local database transaction.
        var stage = "acquire_lock";
        try
        {
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

            stage = "load_rate";
            // Read the locked physical row as JSON. The previous EF projection
            // still failed for legacy rates at load_rate: EF tried to convert
            // columns before the queue could validate the actual snapshot.
            // to_jsonb reads only the requested row without entity hydration
            // and tolerates historical optional column differences.
            string? databaseRow;
            await using (var readCommand = db.Database.GetDbConnection().CreateCommand())
            {
                readCommand.Transaction = transaction.GetDbTransaction();
                readCommand.CommandText =
                    """SELECT to_jsonb(r)::text FROM pricing."RateHeaders" r WHERE r.id = @id""";
                var idParameter = readCommand.CreateParameter();
                idParameter.ParameterName = "@id";
                idParameter.Value = rateId;
                readCommand.Parameters.Add(idParameter);
                databaseRow = await readCommand.ExecuteScalarAsync(cancellationToken) as string;
            }
            if (databaseRow is null)
                return Results.NotFound(new { code = "rate_not_found" });

            var rate = DynamicsQuoteEnqueueRateReader.Parse(databaseRow);
            if (rate.Id != rateId || rate.IsDeleted)
                return Results.NotFound(new { code = "rate_not_found" });
            if (rate.Status != RateStatus.AcceptedByClient.ToString() ||
                string.IsNullOrWhiteSpace(rate.IdtraNumber))
                return Results.Conflict(new
                {
                    code = "rate_not_ready_for_dynamics",
                    message = "La tarifa debe estar aceptada y tener IDTRA antes de crear la oferta."
                });
            if (rate.RevisionNumber < 1)
                return Results.Conflict(new { code = "invalid_rate_revision" });

            stage = "existing_job";
            // All reads in this transaction use the same underlying Npgsql
            // connection and transaction. Do not mix EF LINQ here: production
            // failures moved from load_rate to existing_job after switching the
            // first read to SQL, even though the job table is accessible to the
            // normal queue viewer.
            string? existingStatus;
            await using (var jobCommand = db.Database.GetDbConnection().CreateCommand())
            {
                jobCommand.Transaction = transaction.GetDbTransaction();
                jobCommand.CommandText =
                    """
                    SELECT "Status"
                    FROM pricing."DynamicsQuoteSyncJobs"
                    WHERE "RateId" = @rateId AND "RevisionNumber" = @revision
                    LIMIT 1
                    """;
                var jobRateId = jobCommand.CreateParameter();
                jobRateId.ParameterName = "@rateId";
                jobRateId.Value = rate.Id;
                jobCommand.Parameters.Add(jobRateId);
                var jobRevision = jobCommand.CreateParameter();
                jobRevision.ParameterName = "@revision";
                jobRevision.Value = rate.RevisionNumber;
                jobCommand.Parameters.Add(jobRevision);
                existingStatus = await jobCommand.ExecuteScalarAsync(cancellationToken) as string;
            }
            if (existingStatus is not null)
                return Results.Ok(new
                {
                    rateId, revisionNumber = rate.RevisionNumber,
                    status = existingStatus, alreadyQueued = true,
                    message = "Ya existe una sincronización para esta revisión. No se duplicó."
                });

            stage = "load_equipment";
            // A second EF projection can fail on historical records too. Read
            // only the three required physical columns; build the same JSON
            // shape consumed by DynamicsQuotePayloadBuilder.
            string? containersJson;
            await using (var containerCommand = db.Database.GetDbConnection().CreateCommand())
            {
                containerCommand.Transaction = transaction.GetDbTransaction();
                containerCommand.CommandText =
                    """
                    SELECT COALESCE(
                        jsonb_agg(jsonb_build_object(
                            'containerTypeName', c.container_type_name,
                            'containerTypeCode', c.container_type_code,
                            'quantity', c.quantity
                        ) ORDER BY c.container_type_code),
                        '[]'::jsonb)::text
                    FROM pricing."RateContainerAllocations" c
                    WHERE c.rate_header_id = @rateId
                    """;
                var containerRateId = containerCommand.CreateParameter();
                containerRateId.ParameterName = "@rateId";
                containerRateId.Value = rateId;
                containerCommand.Parameters.Add(containerRateId);
                containersJson = await containerCommand.ExecuteScalarAsync(cancellationToken) as string;
            }

            stage = "snapshot";
            using var containersDocument = JsonDocument.Parse(containersJson ?? "[]");
            var payloadJson = rate.ToPayloadJson(containersDocument.RootElement);
            var jobId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // The manual command is already audited by its immutable,
            // uniquely keyed durable job. DO NOT invoke PricingAuditService
            // with an artificial RateHeaderUpdated/Before=After event: it
            // adds unrelated outbox writes and can roll back manual enqueues.
            // Atomic PostgreSQL ON CONFLICT also handles any concurrent
            // acceptance-created job without producing a 500 or duplicates.
            stage = "insert_job";
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO pricing."DynamicsQuoteSyncJobs"
                    ("Id","EventId","RateId","RevisionNumber","PayloadJson",
                     "Status","Attempts","NextAttemptAtUtc","CreatedAtUtc")
                VALUES ({jobId},{eventId},{rate.Id},{rate.RevisionNumber},
                        {payloadJson},'Pending',0,{now},{now})
                ON CONFLICT ("RateId","RevisionNumber") DO NOTHING
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (inserted == 0)
            {
                // Another acceptance or administrator inserted this revision.
                // Return its real state; never create a second operation.
                // The database unique constraint already proved another job
                // owns this revision. Do not run an EF query after COMMIT merely
                // to display its status; the queue GET provides its actual state.
                return Results.Ok(new
                {
                    rateId, revisionNumber = rate.RevisionNumber,
                    status = "Existing",
                    alreadyQueued = true,
                    message = "Esta revisión ya tiene un trabajo de Dynamics. No se duplicó. Actualizá la cola para consultar el estado."
                });
            }
            logger.LogInformation(
                "Manual Dynamics quote enqueue recorded for rate {RateId} revision {Revision} job {JobId}",
                rateId, rate.RevisionNumber, jobId);
            return Results.Accepted($"/api/pricing/rates/{rateId}/dynamics-sync",
                new
                {
                    rateId, revisionNumber = rate.RevisionNumber,
                    status = "Pending", alreadyQueued = false,
                    message = "Trabajo guardado en Pricing. Dynamics aún no ha confirmado la QUO."
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException error)
        {
            logger.LogWarning(error,
                "Dynamics enqueue refused invalid rate snapshot for {RateId} at {Stage}",
                rateId, stage);
            return Results.Conflict(new
            {
                code = "dynamics_rate_snapshot_invalid",
                stage,
                message = "La tarifa contiene datos históricos inválidos para sincronizar. Revisá el registro de Pricing; no se creó un trabajo ni se envió una QUO."
            });
        }
        catch (Exception error)
        {
            // The production exception stays in server logs. The operator
            // receives a stage, rate, and trace identifier without SQL or
            // sensitive snapshot contents.
            logger.LogError(error,
                "Dynamics manual enqueue failed at {Stage} for rate {RateId} trace {TraceId}",
                stage, rateId, http.TraceIdentifier);
            return Results.Problem(
                title: "No se pudo encolar la tarifa en Pricing.",
                detail: "La operación no fue confirmada. Revisá el registro del servicio con el identificador de seguimiento.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "dynamics_enqueue_failed",
                    ["stage"] = stage,
                    ["traceId"] = http.TraceIdentifier,
                    // Safe diagnostics: never return SQL, internal connection
                    // strings, stack traces or the immutable quote payload.
                    ["exceptionType"] = error.GetType().Name,
                    ["databaseCode"] = (error as Npgsql.PostgresException)?.SqlState
                });
        }
    }


    private sealed record QuoteReadiness(bool Ready, string Code, string Message,
        string[]? MissingGrants = null);

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
                // A 429 is a temporary rate limit, never an authorization
                // failure or evidence that the real Pricing key is invalid.
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    return new(false, "DYNAMICS_PREFLIGHT_RATE_LIMITED",
                        "El conector alcanzó temporalmente su límite de solicitudes. No se envió ninguna QUO; repetí la comprobación cuando se libere la ventana.");
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
            // Field names originate from our authenticated connector. Permit
            // only bounded, identifier-like labels; do not surface arbitrary
            // remote text or request contents from an upstream error response.
            var missingGrants = root.TryGetProperty("missingGrants", out var items) &&
                items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Where(item => item.Length is >= 3 and <= 128 &&
                        System.Text.RegularExpressions.Regex.IsMatch(item,
                            @"^[A-Za-z_][A-Za-z0-9_.:|@-]*$"))
                    .Distinct(StringComparer.Ordinal)
                    .Take(30).ToArray()
                : [];
            // Do not copy arbitrary upstream response text into the admin API.
            return new(enabled,
                codeText is "DYNAMICS_QUOTE_WRITES_READY" or "production_writes_not_approved"
                    or "production_quote_writes_not_approved" or "quote_grants_incomplete"
                    ? codeText : "DYNAMICS_PREFLIGHT_UNKNOWN",
                enabled ? "La clave de Pricing tiene permisos verificados para cabecera, vínculos y línea de venta."
                    : codeText == "quote_grants_incomplete"
                        ? missingGrants.Length > 0
                            ? "La clave de Pricing no tiene autorizados estos permisos: " +
                              string.Join(", ", missingGrants.Take(12)) +
                              ". Usá «Autorizar vínculos y venta total» con la clave activa."
                            : "Faltan permisos en la clave activa de Pricing. Usá «Autorizar vínculos y venta total» y volvé a comprobar."
                        : "Producción todavía rechaza las escrituras de QUO. Se requiere autorización del cambio.",
                missingGrants);
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
            message = check.Message, missingGrants = check.MissingGrants,
            configuredKeyId, noPostExecuted = true });
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

    private sealed record ConfirmedWriteResult(
        bool Confirmed, string? HeaderStatus, string? TotalStatus,
        Guid? QuoteId, Guid? LineId, bool NoPostExecuted);

    private static async Task<IResult> ReconcileConfirmedAsync(
        Guid rateId, HttpContext http, ServiceDbContext db,
        IHttpClientFactory clients, IConfiguration configuration, CancellationToken ct)
    {
        if (!CanApproveRecovery(http.User)) return Results.Forbid();
        var rate = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new { x.Status, x.RevisionNumber })
            .SingleOrDefaultAsync(ct);
        if (rate is null || rate.Status != RateStatus.AcceptedByClient)
            return Results.Conflict(new { code = "sync_rate_not_eligible" });
        var job = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .Where(x => x.RateId == rateId && x.RevisionNumber == rate.RevisionNumber)
            .SingleOrDefaultAsync(ct);
        if (job is null) return Results.NotFound(new { code = "sync_job_not_found" });
        if (job.Status == "Completed")
            return Results.Ok(new { status = "Completed", alreadyReconciled = true });
        if (job.Status != "NeedsAttention")
            return Results.Conflict(new { code = "sync_job_not_ready_for_reconciliation" });
        var apiKey = configuration["DynamicsQuoteSync:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.Conflict(new { code = "dynamics_key_missing" });

        // Never trust a browser-submitted CRM record ID. Recheck the two
        // EXACT durable write operations on the connector using Pricing's key.
        const string prefix =
            "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta";
        var url = prefix + "/write-result?connectionCode=principal&dholeRateId=" +
            Uri.EscapeDataString(rateId.ToString("D")) +
            "&revisionNumber=" + rate.RevisionNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        ConfirmedWriteResult? confirmation;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var response = await clients.CreateClient("DynamicsQuoteSync")
                .SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Results.Conflict(new {
                    code = "sync_reconciliation_ledger_unavailable",
                    message = "No se pudo verificar el registro de idempotencia. No se modificó la tarifa."
                });
            confirmation = await response.Content.ReadFromJsonAsync<ConfirmedWriteResult>(
                cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Results.Conflict(new {
                code = "sync_reconciliation_unavailable",
                message = "No se pudo consultar Dynamics. No se modificó la tarifa."
            });
        }
        if (confirmation is not {
            Confirmed: true, NoPostExecuted: true,
            HeaderStatus: "succeeded", TotalStatus: "succeeded",
            QuoteId: Guid quoteId, LineId: Guid lineId
        } || quoteId == Guid.Empty || lineId == Guid.Empty)
            return Results.Conflict(new {
                code = "sync_reconciliation_not_confirmed",
                message = "No hay dos operaciones confirmadas para esta misma tarifa y revisión. No se enviará otra QUO."
            });

        var now = DateTime.UtcNow;
        var resultJson = JsonSerializer.Serialize(new {
            state = "succeeded", recordId = quoteId, lineRecordId = lineId,
            stage = "completed", reconciledFromLedger = true, noPostExecuted = true
        });
        var affected = await db.DynamicsQuoteSyncJobs
            .Where(x => x.Id == job.Id && x.Status == "NeedsAttention")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Completed")
                .SetProperty(x => x.ResponseJson, resultJson)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null),
                ct);
        return affected == 1
            ? Results.Ok(new {
                status = "Completed", quoteId, lineId,
                message = "QUO y línea de venta conciliadas sin otro POST a Dataverse."
            })
            : Results.Conflict(new { code = "sync_reconciliation_state_changed" });
    }

    private static bool CanApproveRecovery(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        return principal.Claims.Where(c => c.Type is "scope" or "scp" or "scopes")
            .SelectMany(c => c.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scope => scope is "dynamics.security.admin" or "dynamics.*" or "*");
    }

    private static async Task<IResult> RecoverRejectedAsync(
        Guid rateId, HttpContext http,
        ServiceDbContext db, IHttpClientFactory clients,
        IConfiguration configuration, CancellationToken ct)
    {
        if (!CanApproveRecovery(http.User)) return Results.Forbid();
        // The button click is the explicit authorization. The server records
        // a fixed, auditable reason and actor; no free-text or checkbox is
        // required. Ledger and read-only preview still gate every new key.
        var rate = await db.RateHeaders.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == rateId)
            .Select(r => new { r.IsDeleted, r.Status, r.RevisionNumber, r.IdtraNumber })
            .SingleOrDefaultAsync(ct);
        if (rate is null || rate.IsDeleted ||
            rate.Status != RateStatus.AcceptedByClient ||
            string.IsNullOrWhiteSpace(rate.IdtraNumber))
            return Results.Conflict(new {
                code = "rate_not_eligible",
                message = "La tarifa debe estar vigente, aceptada y con IDTRA."
            });
        var job = await db.DynamicsQuoteSyncJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.RateId == rateId &&
                j.RevisionNumber == rate.RevisionNumber, ct);
        if (job is null || job.Status != "NeedsAttention" ||
            job.RecoveryAttempt >= 3)
            return Results.Conflict(new {
                code = "recovery_job_not_eligible",
                message = "No hay una incidencia recuperable o se alcanzó el límite de 3 recuperaciones."
            });

        var apiKey = configuration["DynamicsQuoteSync:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.Conflict(new { code = "dynamics_key_missing" });
        var client = clients.CreateClient("DynamicsQuoteSync");
        var prefix = "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta";
        var nextAttempt = job.RecoveryAttempt + 1;

        // Read-only connector ledger verification, scoped to the actual API
        // key. It also verifies all prior recovery attempts are explicit 4xx
        // rejects with no quoteId and no total lines.
        var ledgerUrl = prefix + "/recovery-eligibility?connectionCode=principal" +
            "&dholeRateId=" + rateId.ToString("D") +
            "&revisionNumber=" + rate.RevisionNumber +
            "&nextAttempt=" + nextAttempt;
        using (var check = new HttpRequestMessage(HttpMethod.Get, ledgerUrl))
        {
            check.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var result = await client.SendAsync(check, ct);
            if (!result.IsSuccessStatusCode)
            {
                // Do not hide the downstream cause as an opaque HTTP 409.
                // Report only the connector's vetted code and status, never
                // raw response payloads, credentials or ledger contents.
                var failure = await result.Content.ReadAsStringAsync(ct);
                var (providerCode, _) =
                    DynamicsQuoteFailureDiagnostics.ReadProviderError(failure);
                return Results.Conflict(new {
                    code = "recovery_ledger_unavailable",
                    providerCode = providerCode ?? "connector_recovery_read_failed",
                    connectorHttpStatus = (int)result.StatusCode,
                    message = providerCode == "quote_write_key_required"
                        ? "El conector no acepta todavía UPSERT para verificar recuperaciones; actualizá Dynamics Connector y volvé a intentar."
                        : providerCode == "crud_key_invalid"
                        ? "La clave activa de Pricing fue rechazada por Dynamics Connector."
                        : "La verificación del historial fue rechazada por Dynamics Connector. Revisá el código y estado del proveedor; no se reenvió la QUO.",
                    noPostExecuted = true
                });
            }
            using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("eligible", out var eligible) ||
                eligible.ValueKind != JsonValueKind.True ||
                !json.RootElement.TryGetProperty("noPostExecuted", out var readOnly) ||
                readOnly.ValueKind != JsonValueKind.True)
                return Results.Conflict(new {
                    code = "recovery_not_safe",
                    message = "El conector no confirmó el rechazo previo sin registro creado."
                });
        }

        // Before reserving a new generation, validate current fields, allowed
        // lookups, text limits and schema; all read-only to Dataverse.
        object payload;
        try
        {
            using var schemaRequest = new HttpRequestMessage(HttpMethod.Get,
                prefix + "/schema?connectionCode=principal");
            schemaRequest.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var schemaResponse = await client.SendAsync(schemaRequest, ct);
            if (!schemaResponse.IsSuccessStatusCode)
                return Results.Conflict(new { code = "recovery_schema_denied" });
            var schema = DynamicsQuotePayloadBuilder.ReadSchema(
                await schemaResponse.Content.ReadAsStringAsync(ct));
            payload = await BuildQuotePayloadForReadOnlyChecksAsync(
                client, apiKey, prefix, job.PayloadJson, schema, configuration, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return Results.Conflict(new { code = "recovery_mapping_invalid",
                message = "La plantilla o los IDTRA no pudieron validarse. Corregí el mapeo antes de recuperar." });
        }

        using (var validate = new HttpRequestMessage(HttpMethod.Post,
            prefix + "/validate-lookups"))
        {
            validate.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            validate.Content = new StringContent(JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json");
            using var result = await client.SendAsync(validate, ct);
            if (!result.IsSuccessStatusCode)
            {
                var (code, _) = DynamicsQuoteFailureDiagnostics.ReadProviderError(
                    await result.Content.ReadAsStringAsync(ct));
                return Results.Conflict(new { code = code ?? "recovery_lookup_denied",
                    message = "La cabecera no está lista: existe un vínculo faltante o sin permiso. Diagnosticá antes de crear." });
            }
            using var check = JsonDocument.Parse(await result.Content.ReadAsStringAsync(ct));
            if (!check.RootElement.TryGetProperty("noPostExecuted", out var readOnly) ||
                readOnly.ValueKind != JsonValueKind.True)
                return Results.Conflict(new { code = "recovery_validation_unconfirmed",
                    message = "No se confirmó la validación de solo lectura. No se reenvió la QUO." });
        }
        var actor = http.User.Claims.FirstOrDefault(c =>
            c.Type is ClaimTypes.NameIdentifier or "sub")?.Value ?? "dynamics-admin";
        if (actor.Length > 128) actor = actor[..128];
        var changed = await db.DynamicsQuoteSyncJobs.Where(j =>
                j.Id == job.Id && j.Status == "NeedsAttention" &&
                j.RecoveryAttempt == job.RecoveryAttempt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.RecoveryAttempt, nextAttempt)
                .SetProperty(j => j.RecoveryReason,
                    "Reenvío solicitado desde Dynamics: HTTP 4xx rechazado, " +
                    "sin cabecera ni línea creadas; historial y mapeo validados.")
                .SetProperty(j => j.RecoveryApprovedBy, actor)
                .SetProperty(j => j.RecoveryApprovedAtUtc, DateTime.UtcNow)
                .SetProperty(j => j.Status, "Pending")
                .SetProperty(j => j.Attempts, 0)
                .SetProperty(j => j.LastError, (string?)null)
                .SetProperty(j => j.ResponseJson, (string?)null)
                .SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow)
                .SetProperty(j => j.LeaseExpiresAtUtc, (DateTime?)null), ct);
        return changed == 1
            ? Results.Accepted($"/api/pricing/rates/{rateId}/dynamics-sync",
                new { status = "Pending", recoveryAttempt = nextAttempt,
                    detail = "Reenvío seguro autorizado; el worker procesa la QUO en su siguiente ciclo (1 segundo). Nueva clave -r" +
                        nextAttempt + ". El POST de esta solicitud no escribe directamente en Dataverse." })
            : Results.Conflict(new { code = "recovery_concurrent_change" });
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
            var body = await BuildQuotePayloadForReadOnlyChecksAsync(
                clients.CreateClient("DynamicsQuoteSync"), apiKey,
                "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta",
                job.PayloadJson, schema, configuration, cancellationToken);
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
                        : providerCode == "quote_customer_required"
                        ? "El conector está usando una versión anterior que todavía exige customerid. Actualizá el conector para enviar QUO sin cliente; no se realizó ningún POST."
                        : providerCode is "client_denied" or "lookup_target_denied" or
                          "lookup_name_field_not_authorized" or "lookup_target_fields_denied"
                        ? "La clave de Pricing no puede consultar una relación requerida. Autorizá la lectura de Cuenta, IDTRA y divisa con «Autorizar vínculos y venta total» para la clave realmente configurada en Pricing. Volvé a diagnosticar antes de recuperar; esta consulta no crea QUO."
                        : "No se pudieron comprobar los vínculos de la oferta en Dynamics.",
                    noPostExecuted = true
                });
            }
            using var previewResult = JsonDocument.Parse(lookupJson);
            var verifiedReadOnly = previewResult.RootElement.ValueKind == JsonValueKind.Object &&
                previewResult.RootElement.TryGetProperty("noPostExecuted", out var proof) &&
                proof.ValueKind == JsonValueKind.True;
            return Results.Ok(new
            {
                valid = verifiedReadOnly, stage = "lookup",
                code = verifiedReadOnly ? "DYNAMICS_LOOKUPS_VALID" :
                    "DYNAMICS_PREVIEW_NOT_VERIFIED",
                schemaVersion = schema.Version,
                customerRequired = false,
                message = verifiedReadOnly
                    ? "IDTRA, divisa, vínculos permitidos y límites de texto verificados sin enviar cliente ni crear QUO."
                    : "El conector no confirmó la validación de solo lectura.",
                noPostExecuted = verifiedReadOnly
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

    /// <summary>
    /// READ-ONLY validation and administrative recovery must use precisely the
    /// same ISO/Dataverse currency resolution as DynamicsQuoteDeliveryWorker.
    /// The old preview omitted currencyId, making legitimate recovery fail
    /// before any customer validation could be completed.
    /// </summary>
    private static async Task<object> BuildQuotePayloadForReadOnlyChecksAsync(
        HttpClient client, string apiKey, string prefix, string snapshot,
        DynamicsCrudSchema schema, IConfiguration configuration, CancellationToken ct)
    {
        var iso = DynamicsQuoteCurrency.ResolveIso(snapshot);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            prefix + "/resolve-currency?connectionCode=principal&currencyCode=" + iso);
        request.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var (code, _) = DynamicsQuoteFailureDiagnostics.ReadProviderError(
                await response.Content.ReadAsStringAsync(ct));
            throw new InvalidOperationException(
                "Dynamics currency resolution rejected: " + (code ?? "currency_lookup_unavailable"));
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        if (!root.TryGetProperty("noPostExecuted", out var noWrite) ||
            noWrite.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("currencyCode", out var actualIso) ||
            actualIso.ValueKind != JsonValueKind.String ||
            actualIso.GetString() != iso ||
            !root.TryGetProperty("currencyId", out var actualId) ||
            actualId.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(actualId.GetString(), out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Currency validation did not return a verified Dataverse GUID.");
        using var original = JsonDocument.Parse(snapshot);
        var sourceCode = original.RootElement.TryGetProperty("currencyCode", out var field) &&
            field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        DynamicsQuoteCurrency.VerifyResolvedIdentity(sourceCode, iso, id);
        var normalized = DynamicsQuoteCurrency.NormalizeSnapshot(snapshot, iso);
        return DynamicsQuotePayloadBuilder.Build(normalized, schema, configuration, id);
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
        // Historical text-limit HTTP 422 is a proven pre-header validation
        // rejection, even when the old worker incorrectly set postAttempted.
        if (DynamicsQuoteFailureDiagnostics.IsKnownLegacyTextPreflight(diagnosticJson))
            return true;
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
