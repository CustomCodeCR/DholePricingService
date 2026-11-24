using System.Security.Claims;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>Operations service additions are reviewed only by Pricing staff.
/// Reviewing a request never rewrites an accepted quotation or grants low-margin approval.</summary>
public static class OperationsPricingReviewEndpoints
{
    public sealed record DecisionRequest(string Outcome, string Reason, long ExpectedVersion);

    public static IEndpointRouteBuilder MapOperationsPricingReviewEndpoints(
        this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/pricing/operations-reviews")
            .RequireAuthorization();

        api.MapGet("/", async (
            int? page, int? pageSize, HttpContext http, ServiceDbContext db,
            CancellationToken ct) =>
        {
            if (!IsPricingReviewer(http.User)) return Results.Forbid();
            var offset = Math.Max(0, (page ?? 1) - 1);
            var limit = Math.Clamp(pageSize ?? 25, 1, 100);
            var query = db.OperationsReviewRequests.AsNoTracking();
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(x => x.Status == "PENDING_REVIEW" ? 0 : 1)
                .ThenByDescending(x => x.RequestedAtUtc)
                .Skip(offset * limit).Take(limit)
                .Select(x => new {
                    x.Id, x.OperationsRequestId, x.Idtra,
                    x.CaseId, x.QuoteId, x.AcceptedOptionId, x.ServiceCode,
                    x.Reason, x.Status, x.RequestedAtUtc, x.DecidedAtUtc,
                    x.DecisionReason, x.Version
                })
                .ToArrayAsync(ct);
            return Results.Ok(new {
                page = offset + 1, pageSize = limit, total, items
            });
        });

        api.MapPost("/{reviewId:guid}/decision", async (
            Guid reviewId, DecisionRequest input,
            HttpContext http, ServiceDbContext db,
            IIntegrationEventOutboxWriter integrationEvents,
            CancellationToken ct) =>
        {
            if (!IsPricingReviewer(http.User)) return Results.Forbid();
            var userId = UserId(http.User);
            if (userId == Guid.Empty) return Results.Forbid();
            var decision = input.Outcome?.Trim().ToUpperInvariant() ?? "";
            if (decision is not ("NO_PRICE_CHANGE_REQUIRED" or "REVISION_REQUIRED" or "REJECTED") ||
                string.IsNullOrWhiteSpace(input.Reason) ||
                input.Reason.Trim().Length is < 10 or > 2000 ||
                input.ExpectedVersion < 1)
                return Results.BadRequest(new { code = "PRICING_OPERATIONS_REVIEW_INVALID" });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var review = await db.OperationsReviewRequests
                .SingleOrDefaultAsync(x => x.Id == reviewId, ct);
            if (review is null) return Results.NotFound();
            if (review.Version != input.ExpectedVersion)
                return Results.Conflict(new { code = "PRICING_OPERATIONS_REVIEW_VERSION_CONFLICT",
                    currentVersion = review.Version });
            if (review.Status is "NO_PRICE_CHANGE_REQUIRED" or "REJECTED")
                return Results.Conflict(new { code = "PRICING_OPERATIONS_REVIEW_ALREADY_DECIDED" });
            review.Status = decision;
            review.DecisionReason = input.Reason.Trim();
            review.DecidedAtUtc = DateTimeOffset.UtcNow;
            review.DecidedBy = userId;
            review.Version++;

            await integrationEvents.WriteAsync(
                "pricing.operations-review.resolved.v1",
                "pricing.operations-review.resolved.v1",
                new {
                    pricingReviewId = review.Id,
                    review.OperationsRequestId, review.CaseId, review.CompanyId,
                    review.ServiceId, review.QuoteId, review.AcceptedOptionId,
                    review.Status, review.DecidedBy, review.DecidedAtUtc,
                    review.DecisionReason, review.Version
                },
                correlationId: $"pricing-review:{review.OperationsRequestId:D}:{review.Version}",
                cancellationToken: ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { code = "PRICING_OPERATIONS_REVIEW_CONCURRENT_CHANGE" });
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new {
                review.Id, review.OperationsRequestId,
                review.Status, review.Version, review.DecidedAtUtc
            });
        });
        return app;
    }

    private static Guid UserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            principal.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    private static bool IsPricingReviewer(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true &&
        (principal.IsInRole("SuperAdmin") || principal.IsInRole("SuperUsuario") ||
         principal.Claims.Where(x => x.Type is "scope" or "scp" or "scopes")
             .SelectMany(x => x.Value.Split([' ', ',', ';'],
                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
             .Any(scope => scope.Equals("pricing.rate.update",
                 StringComparison.OrdinalIgnoreCase) ||
                 scope.Equals("pricing.rate.approve-low-margin",
                     StringComparison.OrdinalIgnoreCase)));
}
