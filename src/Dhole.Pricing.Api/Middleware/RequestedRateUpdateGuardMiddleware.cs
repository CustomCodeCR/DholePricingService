using System.Text.Json;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Auditing;
using Dhole.Pricing.Application.Auditing;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Middleware;

public sealed class RequestedRateUpdateGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ServiceDbContext db, IPricingAuditService audit)
    {
        if (!HttpMethods.IsPut(context.Request.Method) || !TryGetRateId(context.Request.Path, out var rateId))
        {
            await next(context);
            return;
        }

        var rateStatus = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => (RateStatus?)x.Status)
            .FirstOrDefaultAsync(context.RequestAborted);
        if (!rateStatus.HasValue)
        {
            await next(context);
            return;
        }

        var linkedRequests = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == rateId)
            .Select(x => new { x.Id, x.Status })
            .ToListAsync(context.RequestAborted);
        var decision = RateUpdateWindowPolicy.Evaluate(
            rateStatus.Value,
            linkedRequests.Count > 0,
            linkedRequests.Any(x => x.Status == RateRequestStatus.Open));

        if (!decision.CanUpdate)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "Pricing.RateUpdateWindowClosed",
                message = decision.Message,
                rateStatus = rateStatus.Value.ToString(),
            }, context.RequestAborted);
            return;
        }

        context.Request.EnableBuffering();
        string? updateReason = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            updateReason = GetStringIgnoreCase(document.RootElement, "updateReason")
                ?? GetStringIgnoreCase(document.RootElement, "reason");
        }
        catch (JsonException) { }
        finally { context.Request.Body.Position = 0; }

        if (string.IsNullOrWhiteSpace(updateReason) || updateReason.Trim().Length < 5)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "Pricing.RateUpdateReasonRequired",
                message = "Indique un motivo de al menos 5 caracteres para actualizar la tarifa.",
            }, context.RequestAborted);
            return;
        }

        await next(context);
        if (context.Response.StatusCode >= 400) return;

        await audit.PublishAsync(new PricingAuditEvent(
            EventType: "pricing.rate.update-reason",
            Action: PricingAuditActions.Updated,
            EntityType: PricingAuditEntityTypes.RateHeader,
            EntityId: rateId,
            ActorUserId: context.GetCurrentUserId(),
            Payload: new
            {
                UpdateReason = updateReason.Trim(),
                RequestIds = linkedRequests.Select(x => x.Id).ToArray(),
                PreviousStatus = rateStatus.Value.ToString(),
                UpdateWindow = decision.UpdateWindow,
                Source = "RateUpdateGuard",
            }), context.RequestAborted);
    }

    private static bool TryGetRateId(PathString path, out Guid rateId)
    {
        rateId = Guid.Empty;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length != 4
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("pricing", StringComparison.OrdinalIgnoreCase)
            || !segments[2].Equals("rates", StringComparison.OrdinalIgnoreCase)) return false;
        return Guid.TryParse(segments[3], out rateId);
    }

    private static string? GetStringIgnoreCase(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }
        return null;
    }
}
