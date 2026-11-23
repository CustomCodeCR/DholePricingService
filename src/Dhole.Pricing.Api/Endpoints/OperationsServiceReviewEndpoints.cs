using System.Data;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>Pricing owns review decisions for Operations-added services; original QUO stays immutable.</summary>
public static class OperationsServiceReviewEndpoints
{
    public sealed record ResolveReviewRequest(string Decision, string Notes);

    public static IEndpointRouteBuilder MapOperationsServiceReviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/operations-service-reviews", async (
            string? status, int? page, ServiceDbContext db, CancellationToken ct) =>
        {
            var query = db.OperationsServiceReviews.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status.ToUpper());
            var rows = await query.OrderByDescending(x => x.RequestedAtUtc)
                .Skip((Math.Clamp(page ?? 1, 1, 10000) - 1) * 50)
                .Take(50).Select(x => new
                {
                    x.Id, x.QuoteId, x.CaseId, x.Idtra, x.ServiceCode, x.ServiceName,
                    x.Reason, x.Status, x.RequestedAtUtc, x.RequestedBy,
                    x.ResolvedBy, x.ResolvedAtUtc, x.ResolutionNotes
                }).ToArrayAsync(ct);
            return Results.Ok(rows);
        })
            .WithTags("Operations Commercial Review")
            .RequireAuthorization().RequireScope(PricingConstants.Scopes.RateView);

        app.MapPost("/api/pricing/operations-service-reviews/{id:guid}/decision",
            async (Guid id, ResolveReviewRequest request, HttpContext http,
                ServiceDbContext db, IIntegrationEventOutboxWriter outbox,
                CancellationToken ct) =>
            {
                var actor = http.GetCurrentUserId();
                var decision = request.Decision?.Trim().ToUpperInvariant();
                if (actor == Guid.Empty || decision is not ("APPROVED" or "REJECTED") ||
                    string.IsNullOrWhiteSpace(request.Notes) || request.Notes.Length > 2000)
                    return Results.BadRequest(new { code = "PRICING_REVIEW_DECISION_INVALID" });
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var lockingKey = "pricing-ops-review:" + id.ToString("N");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockingKey}, 0));", ct);
                var review = await db.OperationsServiceReviews.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (review is null) return Results.NotFound();
                if (review.Status != "PENDING")
                    return Results.Conflict(new { code = "PRICING_REVIEW_ALREADY_DECIDED", review.Status });
                review.Status = decision!;
                review.ResolvedBy = actor;
                review.ResolvedAtUtc = DateTimeOffset.UtcNow;
                review.ResolutionNotes = request.Notes.Trim();
                await outbox.WriteAsync("pricing.operations.review.resolved.v1",
                    "pricing.operations.review.resolved.v1",
                    new
                    {
                        schemaVersion = 1,
                        reviewRequestId = id,
                        review.CaseId,
                        review.QuoteId,
                        decision,
                        notes = review.ResolutionNotes,
                        resolvedBy = actor,
                        resolvedAtUtc = review.ResolvedAtUtc
                    },
                    "operations-review-resolution:" + id.ToString("N"), ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(new { review.Id, review.Status, review.ResolvedAtUtc });
            })
            .WithTags("Operations Commercial Review")
            .RequireAuthorization().RequireScope(PricingConstants.Scopes.RateUpdate);
        return app;
    }
}
