using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.Pricing.Persistence.Auditing;
using Dhole.Pricing.Persistence.DbContexts;

namespace Dhole.Pricing.Api.Middleware;

public sealed class AuditEndpointMiddleware(
    RequestDelegate next,
    ILogger<AuditEndpointMiddleware> logger
)
{
    private const string SourceService = "DholePricingService";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] IgnoredPathPrefixes =
    [
        "/swagger",
        "/health",
        "/metrics",
        "/favicon.ico",
    ];

    private static readonly string[] EntityIdKeys =
    [
        "id",
        "entityId",
        "costId",
        "rateId",
        "fclRateId",
        "fclDecisionId",
        "decisionId",
        "quotationId",
        "quoteId",
        "carrierId",
        "portId",
        "originPortId",
        "destinationPortId",
        "currencyId",
        "containerTypeId",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (!ShouldAudit(context))
        {
            return;
        }

        try
        {
            var dbContext = context.RequestServices.GetService<ServiceDbContext>();

            if (dbContext is null)
            {
                return;
            }

            var auditContext = AuditExecutionContextAccessor.Current;
            var correlationId = auditContext?.CorrelationId ?? Guid.NewGuid();
            var eventId = Guid.NewGuid();

            var requestPayload = new
            {
                Method = context.Request.Method,
                Path = context.Request.Path.Value,
                QueryString = context.Request.QueryString.Value,
                StatusCode = context.Response.StatusCode,
                Endpoint = context.GetEndpoint()?.DisplayName,
            };

            var metadata = new
            {
                RouteValues = context.Request.RouteValues.ToDictionary(
                    x => x.Key,
                    x => x.Value?.ToString()
                ),
                Query = context.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()),
                TraceIdentifier = context.TraceIdentifier,
            };

            var payload = new
            {
                EventId = eventId,
                CorrelationId = correlationId,
                SourceService,
                EntityType = ResolveEntityType(context),
                EntityId = ResolveEntityId(context),
                Action = ResolveAction(context),
                EventType = ResolveEventType(context),
                UserId = auditContext?.UserId,
                UserName = auditContext?.UserName,
                IpAddress = auditContext?.IpAddress,
                UserAgent = auditContext?.UserAgent,
                OccurredAt = DateTime.UtcNow,
                BeforeJson = (string?)null,
                AfterJson = (string?)null,
                PayloadJson = JsonSerializer.Serialize(requestPayload, JsonOptions),
                Metadata = JsonSerializer.Serialize(metadata, JsonOptions),
                ErrorMessage = context.Response.StatusCode >= 400
                    ? $"HTTP {context.Response.StatusCode}"
                    : null,
                StackTrace = (string?)null,
                Details = Array.Empty<object>(),
            };

            dbContext.OutboxMessages.Add(
                new OutboxMessage
                {
                    EventId = Guid.NewGuid(),
                    EventType = "Dhole.AuditLogs.Contracts.AuditEvents.RegisterAuditEventRequest",
                    EventName = "audit.event.registered",
                    SourceService = SourceService,
                    PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                    HeadersJson = null,
                    CorrelationId = correlationId.ToString(),
                    Status = OutboxMessageStatus.Pending,
                    RetryCount = 0,
                    ErrorMessage = null,
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );

            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Request aborted. Do not fail the pipeline because of audit.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to create pricing audit event for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path.Value
            );
        }
    }

    private static bool ShouldAudit(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        if (IgnoredPathPrefixes.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // La auditoría de negocio se genera en los command handlers.
        // El middleware solo conserva eventos técnicos de seguridad/error.
        var statusCode = context.Response.StatusCode;

        return statusCode == StatusCodes.Status401Unauthorized
            || statusCode == StatusCodes.Status403Forbidden
            || statusCode >= StatusCodes.Status500InternalServerError;
    }

    private static string ResolveAction(HttpContext context)
    {
        return context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            >= StatusCodes.Status500InternalServerError => "http_error",
            _ => "http_event",
        };
    }

    private static string ResolveEventType(HttpContext context)
    {
        return context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "pricing.access.unauthorized",
            StatusCodes.Status403Forbidden => "pricing.access.forbidden",
            >= StatusCodes.Status500InternalServerError => "pricing.http.error",
            _ => "pricing.http.event",
        };
    }

    private static string? ResolveEntityType(HttpContext context)
    {
        var segments = context
            .Request.Path.Value?.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments is null || segments.Length == 0)
        {
            return null;
        }

        var apiIndex = Array.FindIndex(
            segments,
            x => x.Equals("api", StringComparison.OrdinalIgnoreCase)
        );

        if (apiIndex < 0 || apiIndex + 1 >= segments.Length)
        {
            return segments.LastOrDefault();
        }

        return ToEntityType(segments[apiIndex + 1]);
    }

    private static string ToEntityType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "pricing" => "Pricing",
            "cost" => "Cost",
            "costs" => "Cost",

            "rates" => "Rate",
            "rate" => "Rate",

            "fcl" => "Fcl",
            "fcl-rates" => "FclRate",
            "fcl-rate" => "FclRate",

            "fcl-decisions" => "FclDecision",
            "fcl-decision" => "FclDecision",
            "decisions" => "FclDecision",
            "decision" => "FclDecision",

            "quotes" => "Quotation",
            "quote" => "Quotation",
            "quotations" => "Quotation",
            "quotation" => "Quotation",

            _ => value,
        };
    }

    private static Guid? ResolveEntityId(HttpContext context)
    {
        foreach (var key in EntityIdKeys)
        {
            if (
                context.Request.RouteValues.TryGetValue(key, out var routeValue)
                && Guid.TryParse(routeValue?.ToString(), out var routeGuid)
            )
            {
                return routeGuid;
            }

            if (
                context.Request.Query.TryGetValue(key, out var queryValue)
                && Guid.TryParse(queryValue.ToString(), out var queryGuid)
            )
            {
                return queryGuid;
            }
        }

        var segments =
            context.Request.Path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        foreach (var segment in segments)
        {
            if (Guid.TryParse(segment, out var guid))
            {
                return guid;
            }
        }

        return null;
    }
}
