using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dhole.Pricing.Api.Endpoints;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Middleware;

public sealed class IdempotencyMiddleware
{
    public const string HeaderName = "Idempotency-Key";
    private const string ReplayHeaderName = "Idempotency-Replayed";
    private static readonly TimeSpan ProcessingLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ExplicitRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan AutomaticRetention = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ConcurrentWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyMiddleware> logger
    )
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentCreateMetadata>() is null)
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        var requestHash = await ComputeRequestHashAsync(context.Request, context.RequestAborted);
        var suppliedKey = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        var explicitKey = !string.IsNullOrWhiteSpace(suppliedKey);

        if (explicitKey && suppliedKey!.Length > 200)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Pricing.IdempotencyKeyInvalid",
                "Idempotency-Key no puede superar 200 caracteres."
            );
            return;
        }

        var publicKey = explicitKey
            ? suppliedKey!
            : $"auto-{requestHash[..32].ToLowerInvariant()}";
        var actor = ResolveActor(context);
        var keyHash = Sha256Hex($"{actor}\n{publicKey}");

        context.Response.Headers[HeaderName] = publicKey;

        if (await TryStartAsync(keyHash, requestHash, context.RequestAborted))
        {
            await ExecuteAndStoreAsync(context, keyHash, requestHash, explicitKey);
            return;
        }

        var deadline = DateTimeOffset.UtcNow.Add(ConcurrentWait);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var existing = await GetAsync(keyHash, context.RequestAborted);
            if (existing is null)
            {
                if (await TryStartAsync(keyHash, requestHash, context.RequestAborted))
                {
                    await ExecuteAndStoreAsync(context, keyHash, requestHash, explicitKey);
                    return;
                }
            }
            else
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status409Conflict,
                        "Pricing.IdempotencyKeyReuse",
                        "La misma Idempotency-Key fue utilizada con un contenido diferente."
                    );
                    return;
                }

                if (string.Equals(existing.State, "Completed", StringComparison.Ordinal))
                {
                    await ReplayAsync(context, existing);
                    return;
                }
            }

            await Task.Delay(PollInterval, context.RequestAborted);
        }

        context.Response.Headers["Retry-After"] = "1";
        await WriteErrorAsync(
            context,
            StatusCodes.Status409Conflict,
            "Pricing.IdempotencyRequestInProgress",
            "Ya existe una solicitud de creación con esta Idempotency-Key en procesamiento. Reintente con la misma clave."
        );
    }

    private async Task ExecuteAndStoreAsync(
        HttpContext context,
        string keyHash,
        string requestHash,
        bool explicitKey
    )
    {
        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        context.Response.Headers[ReplayHeaderName] = "false";

        try
        {
            await _next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            try
            {
                await AbandonAsync(keyHash, requestHash, CancellationToken.None);
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(
                    cleanupError,
                    "No se pudo liberar la Idempotency-Key {KeyHash} luego de un error.",
                    keyHash
                );
            }

            throw;
        }

        try
        {
            responseBuffer.Position = 0;
            var responseBytes = responseBuffer.ToArray();

            if (context.Response.StatusCode < StatusCodes.Status500InternalServerError)
            {
                try
                {
                    await CompleteAsync(
                        keyHash,
                        requestHash,
                        context.Response.StatusCode,
                        responseBytes,
                        context.Response.ContentType,
                        context.Response.Headers["Location"].ToString(),
                        explicitKey ? ExplicitRetention : AutomaticRetention,
                        CancellationToken.None
                    );
                }
                catch (Exception persistenceError)
                {
                    // Never remove a Processing key after the business operation succeeded.
                    // Keeping it blocks a duplicate create until its processing TTL expires.
                    _logger.LogError(
                        persistenceError,
                        "La creación finalizó, pero no se pudo persistir su respuesta idempotente para {KeyHash}.",
                        keyHash
                    );
                }
            }
            else
            {
                await AbandonAsync(keyHash, requestHash, CancellationToken.None);
            }

            context.Response.Body = originalBody;
            await context.Response.Body.WriteAsync(responseBytes.AsMemory(), context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task<string> ComputeRequestHashAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var prefix = Encoding.UTF8.GetBytes(
            $"{request.Method}\n{request.Path}\n{request.QueryString}\n{request.ContentType}\n"
        );
        hash.AppendData(prefix);

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            request.Body.Position = 0;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ResolveActor(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue("user_id")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private async Task<bool> TryStartAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM pricing."ApiIdempotencyRequests"
                WHERE expires_at_utc <= now();
                """;
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pricing."ApiIdempotencyRequests"
            (
                key_hash,
                request_hash,
                state,
                created_at_utc,
                expires_at_utc
            )
            VALUES
            (
                @key_hash,
                @request_hash,
                'Processing',
                now(),
                @expires_at_utc
            )
            ON CONFLICT (key_hash) DO NOTHING
            RETURNING key_hash;
            """;
        Add(command, "key_hash", keyHash);
        Add(command, "request_hash", requestHash);
        Add(command, "expires_at_utc", DateTimeOffset.UtcNow.Add(ProcessingLifetime));

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<IdempotencyRecord?> GetAsync(
        string keyHash,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                request_hash,
                state,
                response_status,
                response_body_base64,
                response_content_type,
                response_location
            FROM pricing."ApiIdempotencyRequests"
            WHERE key_hash = @key_hash
              AND expires_at_utc > now()
            LIMIT 1;
            """;
        Add(command, "key_hash", keyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new IdempotencyRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5)
        );
    }

    private async Task CompleteAsync(
        string keyHash,
        string requestHash,
        int statusCode,
        byte[] responseBody,
        string? contentType,
        string? location,
        TimeSpan retention,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."ApiIdempotencyRequests"
            SET state = 'Completed',
                response_status = @response_status,
                response_body_base64 = @response_body_base64,
                response_content_type = @response_content_type,
                response_location = @response_location,
                completed_at_utc = now(),
                expires_at_utc = @expires_at_utc
            WHERE key_hash = @key_hash
              AND request_hash = @request_hash
              AND state = 'Processing';
            """;
        Add(command, "response_status", statusCode);
        Add(command, "response_body_base64", Convert.ToBase64String(responseBody));
        Add(command, "response_content_type", string.IsNullOrWhiteSpace(contentType) ? null : contentType);
        Add(command, "response_location", string.IsNullOrWhiteSpace(location) ? null : location);
        Add(command, "expires_at_utc", DateTimeOffset.UtcNow.Add(retention));
        Add(command, "key_hash", keyHash);
        Add(command, "request_hash", requestHash);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException(
                "No se pudo completar el registro de idempotencia de la solicitud."
            );
        }
    }

    private async Task AbandonAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM pricing."ApiIdempotencyRequests"
            WHERE key_hash = @key_hash
              AND request_hash = @request_hash
              AND state = 'Processing';
            """;
        Add(command, "key_hash", keyHash);
        Add(command, "request_hash", requestHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        var body = string.IsNullOrWhiteSpace(record.ResponseBodyBase64)
            ? Array.Empty<byte>()
            : Convert.FromBase64String(record.ResponseBodyBase64);

        context.Response.StatusCode = record.ResponseStatus ?? StatusCodes.Status200OK;
        if (!string.IsNullOrWhiteSpace(record.ResponseContentType))
            context.Response.ContentType = record.ResponseContentType;
        if (!string.IsNullOrWhiteSpace(record.ResponseLocation))
            context.Response.Headers["Location"] = record.ResponseLocation;

        context.Response.Headers[ReplayHeaderName] = "true";
        context.Response.ContentLength = body.LongLength;
        if (body.Length > 0)
            await context.Response.Body.WriteAsync(body.AsMemory(), context.RequestAborted);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message
    )
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new
            {
                success = false,
                code,
                message,
                errors = Array.Empty<object>(),
                traceId = context.TraceIdentifier,
            },
            context.RequestAborted
        );
    }

    private static async Task EnsureOpenAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record IdempotencyRecord(
        string RequestHash,
        string State,
        int? ResponseStatus,
        string? ResponseBodyBase64,
        string? ResponseContentType,
        string? ResponseLocation
    );
}
