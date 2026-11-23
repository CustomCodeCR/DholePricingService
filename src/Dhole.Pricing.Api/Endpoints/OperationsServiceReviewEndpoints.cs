using System.Security.Claims;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Pricing exclusively resolves unquoted service reviews; no endpoint silently
/// changes prices, approves margin or alters the accepted commercial snapshot.
/// </summary>
public static class OperationsServiceReviewEndpoints
{
    public sealed record Resolution(string Decision, string Reason);
    public static IEndpointRouteBuilder MapOperationsServiceReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/pricing/operations-service-reviews")
            .WithTags("Operations commercial reviews").RequireAuthorization();

        api.MapGet("/", async (string? status, Guid? quoteId,
            int? page, int? pageSize, ServiceDbContext db, CancellationToken ct) =>
        {
            var query = db.OperationsServiceReviews.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToUpperInvariant();
                if (normalized is not ("PENDING_PRICING" or "APPROVED" or "REJECTED" or "NEEDS_INFORMATION"))
                    return Results.BadRequest(new { code = "PRICING_REVIEW_STATUS_INVALID" });
                query = query.Where(x => x.Status == normalized);
            }
            if (quoteId.HasValue) query = query.Where(x => x.QuoteId == quoteId.Value);
            var n = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            return Results.Ok(new {
                page = n, pageSize = size, total = await query.CountAsync(ct),
                items = await query.OrderByDescending(x => x.RequestedAtUtc)
                    .ThenBy(x => x.Id).Skip((n - 1) * size).Take(size)
                    .Select(x => new { x.Id, x.CaseId, x.QuoteId, x.QuoteGroupId,
                        x.ServiceCatalogId, x.ServiceCode, x.ServiceName, x.Reason,
                        x.RequestedBy, x.RequestedAtUtc, x.Status, x.ResolutionReason,
                        x.ResolvedBy, x.ResolvedAtUtc }).ToArrayAsync(ct)
            });
        }).RequireScope(PricingConstants.Scopes.RateView);

        api.MapPost("/{id:guid}/resolve", async (Guid id, Resolution request,
            HttpContext http, ServiceDbContext db,
            IIntegrationEventOutboxWriter outbox, CancellationToken ct) =>
        {
            var actorRaw = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                http.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(actorRaw, out var actor) || actor == Guid.Empty)
                return Results.Forbid();
            var decision = request.Decision?.Trim().ToUpperInvariant();
            if (decision is not ("APPROVED" or "REJECTED" or "NEEDS_INFORMATION") ||
                string.IsNullOrWhiteSpace(request.Reason) ||
                request.Reason.Length > 2000)
                return Results.BadRequest(new { code = "PRICING_REVIEW_DECISION_INVALID" });
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var review = await db.OperationsServiceReviews
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (review is null) return Results.NotFound();
            if (review.Status != "PENDING_PRICING")
                return Results.Ok(new { review.Id, review.Status, duplicate = true });
            review.Status = decision;
            review.ResolutionReason = request.Reason.Trim();
            review.ResolvedBy = actor;
            review.ResolvedAtUtc = DateTimeOffset.UtcNow;
            await outbox.WriteAsync(
                "pricing.operations.service-review-resolved.v1",
                "pricing.operations.service-review-resolved.v1",
                new {
                    schemaVersion = 1, reviewRequestId = review.Id,
                    review.CaseId, review.QuoteId, decision,
                    reason = review.ResolutionReason, resolvedBy = actor,
                    resolvedAtUtc = review.ResolvedAtUtc
                },
                correlationId: $"operations.service.review:{review.Id:N}",
                cancellationToken: ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { review.Id, review.Status,
                review.ResolutionReason, review.ResolvedBy,
                review.ResolvedAtUtc, changesQuote = false });
        }).RequireScope(PricingConstants.Scopes.RateUpdate);
        return app;
    }
}
