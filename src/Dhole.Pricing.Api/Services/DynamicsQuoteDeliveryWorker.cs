using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Claims accepted-rate revisions atomically, then calls the narrowly scoped Dhole
/// Dynamics template with one stable idempotency key per acceptance. No API key or
/// Dataverse write is placed in a Pricing request, audit event, or source file.
/// </summary>
public sealed class DynamicsQuoteDeliveryWorker(
    IServiceScopeFactory scopes, IHttpClientFactory clients,
    IConfiguration configuration, ILogger<DynamicsQuoteDeliveryWorker> logger)
    : BackgroundService
{
    private const int MaxAttempts = 10;
    private const string Url =
        "https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta";
    // Readiness blocks QUO creation when Dynamics permissions are incomplete.
    // Recheck quickly during a block; use a longer interval when healthy.
    private static readonly TimeSpan HealthyReadinessInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BlockedReadinessInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A missing secret must preserve queued approvals instead of issuing
        // unauthenticated requests or accidentally creating malformed Quotes.
        if (!configuration.GetValue("DynamicsQuoteSync:Enabled", true)) return;
        var apiKey = configuration["DynamicsQuoteSync:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Dynamics quote synchronization is awaiting its backend API key.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        // Repair only historic failures that explicitly prove no POST was
        // attempted. Legacy unclassified failures remain untouched.
        try
        {
            await using var retryScope = scopes.CreateAsyncScope();
            var retryDb = retryScope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            await retryDb.Database.ExecuteSqlRawAsync(
                """
                UPDATE pricing."DynamicsQuoteSyncJobs"
                   SET "Status" = 'Retry',
                       "NextAttemptAtUtc" = now(),
                       "LastError" = NULL
                 WHERE "Status" = 'NeedsAttention'
                   AND "LastError" LIKE '%Published CRUD template is missing msdyn_invoicesetuptotals%'
                   AND "ResponseJson"::jsonb->>'postAttempted' = 'false'
                """, stoppingToken);
            // Recover currency-label failures automatically ONLY when the old
            // diagnostic proves no Dataverse POST occurred. Do not touch
            // partial, ambiguous, completed or unclassified operations.
            // Keep the existing job, revision and idempotency identity.
            var recovered = await retryDb.Database.ExecuteSqlRawAsync(
                """
                UPDATE pricing."DynamicsQuoteSyncJobs" j
                   SET "Status" = 'Retry',
                       "NextAttemptAtUtc" = now(),
                       "LastError" = NULL,
                       "LeaseExpiresAtUtc" = NULL
                 WHERE j."Status" = 'NeedsAttention'
                   AND j."ResponseJson" IS NOT NULL
                   AND j."ResponseJson"::jsonb->>'stage' = 'schema'
                   AND j."ResponseJson"::jsonb->>'code' = 'DYNAMICS_SCHEMA_INVALID'
                   AND j."ResponseJson"::jsonb->>'postAttempted' = 'false'
                   AND j."LastError" LIKE '%The quote ISO currency must be USD or CRC%'
                   AND EXISTS (
                       SELECT 1 FROM pricing."RateHeaders" r
                        WHERE r.id = j."RateId"
                          AND r.revision_number = j."RevisionNumber"
                          AND r.status = 'AcceptedByClient'
                          AND r.is_deleted = false
                          AND NULLIF(btrim(r.idtra_number), '') IS NOT NULL
                   )
                """, stoppingToken);
            if (recovered > 0)
                logger.LogInformation(
                    "Resumed {Count} confirmed pre-write Dynamics currency failures with original idempotency keys.",
                    recovered);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not enqueue previously diagnosed pre-write failures: {ErrorType}.",
                ex.GetType().Name);
        }
        var quoteWritesReady = false;
        var nextReadinessCheckUtc = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Keep approved acceptance jobs queued without consuming attempts
            // while the connector is still in production read-only mode.
            if (DateTime.UtcNow >= nextReadinessCheckUtc)
            {
                quoteWritesReady = await ProbeWriteReadinessAsync(apiKey, stoppingToken);
                nextReadinessCheckUtc = DateTime.UtcNow.Add(quoteWritesReady
                    ? HealthyReadinessInterval : BlockedReadinessInterval);
                if (!quoteWritesReady)
                    logger.LogWarning(
                        "Dynamics quote queue paused: production write approval or connector readiness is missing.");
            }
            try
            {
                // Drain ready jobs back-to-back. Every job is atomically
                // claimed with FOR UPDATE SKIP LOCKED and keeps the same
                // idempotency key: this does not create duplicate Quotes.
                if (quoteWritesReady && await ProcessNextAsync(apiKey, stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Dynamics quote worker iteration failed ({Type}).",
                    ex.GetType().Name);
            }
            try { await Task.Delay(Poll, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> ProbeWriteReadinessAsync(string apiKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                Url + "/write-readiness?connectionCode=principal");
            request.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var response = await clients.CreateClient("DynamicsQuoteSync")
                .SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return false;
            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct));
            var root = body.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("ready", out var value) &&
                value.ValueKind == JsonValueKind.True;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Failed health checks must not dispatch writes. No tokens or
            // HTTP bodies from the remote service are logged.
            return false;
        }
    }

    private async Task<bool> ProcessNextAsync(string apiKey, CancellationToken ct)
    {
        Guid? jobId = null;
        await using (var claimScope = scopes.CreateAsyncScope())
        {
            var db = claimScope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE pricing."DynamicsQuoteSyncJobs"
                   SET "Status" = 'NeedsAttention',
                       "LastError" = 'Previous delivery timed out after maximum attempts.',
                       "LeaseExpiresAtUtc" = NULL
                 WHERE "Status" = 'Processing' AND "Attempts" >= 10
                   AND "LeaseExpiresAtUtc" < now()
                """, ct);
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText =
                    """
                    UPDATE pricing."DynamicsQuoteSyncJobs"
                       SET "Status" = 'Processing',
                           "Attempts" = "Attempts" + 1,
                           "LeaseExpiresAtUtc" = now() + interval '3 minutes'
                     WHERE "Id" = (
                         SELECT "Id" FROM pricing."DynamicsQuoteSyncJobs"
                          WHERE (("Status" IN ('Pending','Retry')
                                  AND "NextAttemptAtUtc" <= now())
                              OR ("Status" = 'Processing'
                                  AND "LeaseExpiresAtUtc" < now()))
                            AND "Attempts" < 10
                          ORDER BY "CreatedAtUtc"
                          LIMIT 1 FOR UPDATE SKIP LOCKED
                     )
                    RETURNING "Id"
                    """;
                jobId = await command.ExecuteScalarAsync(ct) is Guid id ? id : null;
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }

        if (jobId is null) return false;
        await using var scope = scopes.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var job = await dbContext.DynamicsQuoteSyncJobs
            .SingleAsync(x => x.Id == jobId, ct);

        var stage = "schema";
        var postAttempted = false;
        HttpStatusCode? httpStatus = null;
        string? providerCode = null;
        string? providerTitle = null;
        string? providerLookupInput = null;
        string? providerLookupSearchField = null;
        try
        {
            var client = clients.CreateClient("DynamicsQuoteSync");
            using var schemaRequest = new HttpRequestMessage(HttpMethod.Get,
                Url + "/schema?connectionCode=principal");
            schemaRequest.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var schemaResponse = await client.SendAsync(schemaRequest, ct);
            if (!schemaResponse.IsSuccessStatusCode)
            {
                httpStatus = schemaResponse.StatusCode;
                (providerCode, providerTitle) = DynamicsQuoteFailureDiagnostics.ReadProviderError(
                    await schemaResponse.Content.ReadAsStringAsync(ct));
                throw new DeliveryException(
                    "Schema request HTTP " + (int)schemaResponse.StatusCode,
                    Transient(schemaResponse.StatusCode));
            }
            var schemaText = await schemaResponse.Content.ReadAsStringAsync(ct);
            var schema = DynamicsQuotePayloadBuilder.ReadSchema(schemaText);
            using var source = JsonDocument.Parse(job.PayloadJson);
            if (!source.RootElement.TryGetProperty("totalSaleAmount", out var saleElement) ||
                !saleElement.TryGetDecimal(out var totalSaleAmount) || totalSaleAmount < 0 ||
                !source.RootElement.TryGetProperty("totalCostAmount", out var costElement) ||
                !costElement.TryGetDecimal(out var totalCostAmount) || totalCostAmount < 0)
                throw new InvalidOperationException("Rate snapshot has no valid Pricing sale or supplier cost.");

            // The tenant default currency can be CRC even when the Pricing
            // quote is USD. Resolve ISO -> actual Dataverse GUID on every
            // attempt, with the same active integration key (read-only).
            // Currency is a required field of the trusted Pricing integration,
            // independent of the editable crear_oferta mapping list.
            stage = "currency";
            // Pricing keeps catalog codes (CUR-...), whereas Dataverse
            // requires ISO 4217. Use the approved human-readable currency
            // name as a fallback; refuse unknown or conflicting currencies.
            var iso = DynamicsQuoteCurrency.ResolveIso(job.PayloadJson);
            var originalCurrencyCode =
                source.RootElement.TryGetProperty("currencyCode", out var currency) &&
                currency.ValueKind == JsonValueKind.String
                    ? currency.GetString() : null;
            var outboundSnapshot = DynamicsQuoteCurrency.NormalizeSnapshot(job.PayloadJson, iso);
            using var currencyRequest = new HttpRequestMessage(HttpMethod.Get,
                Url + "/resolve-currency?connectionCode=principal&currencyCode=" + iso);
            currencyRequest.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            using var currencyResponse = await client.SendAsync(currencyRequest, ct);
            if (!currencyResponse.IsSuccessStatusCode)
            {
                httpStatus = currencyResponse.StatusCode;
                (providerCode, providerTitle) = DynamicsQuoteFailureDiagnostics.ReadProviderError(
                    await currencyResponse.Content.ReadAsStringAsync(ct));
                throw new DeliveryException(
                    "ISO currency lookup HTTP " + (int)currencyResponse.StatusCode,
                    Transient(currencyResponse.StatusCode));
            }
            using var resolvedCurrency = JsonDocument.Parse(
                await currencyResponse.Content.ReadAsStringAsync(ct));
            if (!resolvedCurrency.RootElement.TryGetProperty("noPostExecuted", out var noWrite) ||
                noWrite.ValueKind != JsonValueKind.True ||
                !resolvedCurrency.RootElement.TryGetProperty("currencyCode", out var resolvedIso) ||
                resolvedIso.GetString() != iso ||
                !resolvedCurrency.RootElement.TryGetProperty("currencyId", out var resolvedId) ||
                !Guid.TryParse(resolvedId.GetString(), out var currencyId) ||
                currencyId == Guid.Empty)
                throw new InvalidOperationException("Dynamics ISO currency lookup returned an invalid GUID.");
            DynamicsQuoteCurrency.VerifyResolvedIdentity(originalCurrencyCode, iso, currencyId);
            stage = "mapping";
            var body = DynamicsQuotePayloadBuilder.Build(outboundSnapshot, schema, configuration, currencyId, job.RateId);

            // Two separately idempotent Dataverse writes: create Quote header
            // and create its one write-in quotedetail for the standard totalamount.
            // Never send the sale amount as a directly writable totalamount.
            var inputJson = JsonSerializer.SerializeToElement(body, Json)
                .GetProperty("inputs");
            var payload = new
            {
                connectionCode = "principal",
                dholeRateId = job.RateId,
                totalSaleAmount,
                totalCostAmount,
                currencyCode = iso,
                currencyId,
                inputs = inputJson
            };
            stage = "execute";

            using var execute = new HttpRequestMessage(HttpMethod.Post,
                Url + "/execute-with-total")
            {
                Content = JsonContent.Create(payload, options: Json)
            };
            execute.Headers.TryAddWithoutValidation("X-Dhole-Api-Key", apiKey);
            execute.Headers.TryAddWithoutValidation("Idempotency-Key",
                "dhole-pricing-quote-" + job.RateId.ToString("N") + "-v" + job.RevisionNumber +
                (job.RecoveryAttempt > 0 ? "-r" + job.RecoveryAttempt : ""));
            postAttempted = true;
            using var result = await client.SendAsync(execute, ct);
            if (!result.IsSuccessStatusCode)
            {
                httpStatus = result.StatusCode;
                var providerResponse = await result.Content.ReadAsStringAsync(ct);
                (providerCode, providerTitle) =
                    DynamicsQuoteFailureDiagnostics.ReadProviderError(providerResponse);
                (providerLookupInput, providerLookupSearchField) =
                    DynamicsQuoteFailureDiagnostics.ReadProviderLookup(providerResponse);
                // The connector explicitly guarantees these failures are
                // BEFORE header creation. Safe to retry after fixing grants;
                // a failure after header creation has a distinct reconciliation code.
                if (DynamicsQuoteFailureDiagnostics.IsConfirmedPreWriteFailure(providerCode))
                    postAttempted = false;
                throw new DeliveryException(
                    "Template execution HTTP " + (int)result.StatusCode,
                    // An ambiguous/409 response may mean the write was applied remotely.
                    // Never generate a new idempotency key.
                    Transient(result.StatusCode) && result.StatusCode != HttpStatusCode.Conflict);
            }
            var response = await result.Content.ReadAsStringAsync(ct);
            using var parsed = JsonDocument.Parse(response);
            if (parsed.RootElement.TryGetProperty("state", out var state) &&
                state.GetString() == "succeeded")
            {
                job.Status = "Completed";
                job.ResponseJson = response.Length > 2000 ? response[..2000] : response;
                job.LastError = null;
                job.CompletedAtUtc = DateTime.UtcNow;
                job.LeaseExpiresAtUtc = null;
                await dbContext.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Dynamics quote synchronized for Pricing rate {RateId} revision {Revision}.",
                    job.RateId, job.RevisionNumber);
                return true;
            }
            throw new DeliveryException("Dynamics did not confirm a succeeded write.", false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var retryable = ex is HttpRequestException or TaskCanceledException ||
                ex is DeliveryException { CanRetry: true };
            job.Status = retryable && job.Attempts < MaxAttempts ? "Retry" : "NeedsAttention";
            var (error, diagnostic) = DynamicsQuoteFailureDiagnostics.Format(
                stage, ex, postAttempted, httpStatus, providerCode, providerTitle,
                providerLookupInput, providerLookupSearchField);
            job.LastError = error;
            job.ResponseJson = diagnostic;
            job.LeaseExpiresAtUtc = null;
            job.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(
                Math.Min(3600, Math.Pow(2, Math.Min(job.Attempts, 10)) * 10));
            await dbContext.SaveChangesAsync(ct);
            logger.LogWarning(
                "Dynamics quote {RateId} revision {Revision}: {Status}, attempt {Attempt}, stage {Stage}, HTTP {HttpStatus}, code {ErrorCode}, postAttempted {PostAttempted}; {Error}.",
                job.RateId, job.RevisionNumber, job.Status, job.Attempts, stage,
                (int?)httpStatus, providerCode, postAttempted, job.LastError);
        }
        // The claimed job was handled (success or persisted failure);
        // check the next queued job immediately instead of sleeping.
        return true;
    }

    private static bool Transient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private sealed class DeliveryException(string message, bool canRetry)
        : Exception(message)
    {
        public bool CanRetry { get; } = canRetry;
    }
}
