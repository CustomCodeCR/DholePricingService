using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Operational reporting for Pricing and Sales. The API enforces seller visibility before
/// returning detail rows, so Excel exports and dashboards share an authorized data source.
/// </summary>
public static class RateOperationsReportEndpoints
{
    public static IEndpointRouteBuilder MapRateOperationsReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/rate-requests/operational-report", GetAsync)
            .WithTags("Rate requests")
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetAsync(
        DateTime? from,
        DateTime? to,
        Guid? sellerId,
        string? mode,
        ServiceDbContext db,
        SellerVisibilityService visibilityService,
        SellerRateAccessService sellerRateAccess,
        HttpContext context,
        CancellationToken ct)
    {
        var currentUserId = context.GetCurrentUserId();
        if (currentUserId is null || currentUserId == Guid.Empty)
            return Results.Unauthorized();

        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return Results.BadRequest(new { message = "La fecha inicial no puede superar a la final." });

        // Rates in the Pricing operations dashboard require Pricing authority; a generic
        // rate.view permission must never grant access to every seller's information.
        var canReadAll = visibilityService.HasScope(context.User, PricingConstants.Scopes.RateRequestReportViewAll)
            || visibilityService.HasScope(context.User, PricingConstants.Scopes.RateRequestViewAll)
            || visibilityService.HasScope(context.User, PricingConstants.Scopes.RateUpdate)
            || visibilityService.HasScope(context.User, PricingConstants.Scopes.RateCreate);

        var visibility = canReadAll
            ? new SellerVisibilityContext(currentUserId.Value, SellerVisibilityMode.All, new HashSet<Guid>())
            : await visibilityService.ResolveAsync(context, requireElevated: false, ct);

        if (visibility is null)
            return Results.Forbid();

        IQueryable<Dhole.Pricing.Domain.Rates.Entities.RateRequest> visibleRequests =
            db.RateRequests.AsNoTracking();

        if (visibility.Mode != SellerVisibilityMode.All)
        {
            var ids = visibility.SellerUserIds.ToArray();
            visibleRequests = visibleRequests.Where(request =>
                request.SellerUserId.HasValue && ids.Contains(request.SellerUserId.Value));
        }

        if (sellerId is { } requestedSeller)
            visibleRequests = visibleRequests.Where(request => request.SellerUserId == requestedSeller);

        if (!string.IsNullOrWhiteSpace(mode))
        {
            var selectedMode = mode.Trim();
            visibleRequests = visibleRequests.Where(request => request.ShipmentMode == selectedMode);
        }

        // Keep all open requests on the dashboard regardless of report period: otherwise
        // the oldest overdue items would disappear when the default range is applied.
        // Completed/cancelled requests are restricted by their original submission date.
        var fromUtc = from?.Date;
        var toExclusive = to?.Date.AddDays(1);
        var history = visibleRequests.Where(request =>
            request.Status == RateRequestStatus.Open
            || ((!fromUtc.HasValue || request.RequestedAtUtc >= fromUtc.Value)
                && (!toExclusive.HasValue || request.RequestedAtUtc < toExclusive.Value)));

        var requests = await history.OrderByDescending(request => request.RequestedAtUtc)
            .ToListAsync(ct);

        var responseRateIds = requests.Where(request => request.RateId.HasValue)
            .Select(request => request.RateId!.Value).Distinct().ToArray();
        var quoteNumbers = await db.RateHeaders.AsNoTracking()
            .Where(rate => responseRateIds.Contains(rate.Id) && !rate.IsDeleted)
            .Select(rate => new { rate.Id, rate.QuoNumber, rate.RateCode })
            .ToDictionaryAsync(rate => rate.Id, rate => rate.QuoNumber ?? rate.RateCode, ct);

        // Fetch request-to-rate associations separately without applying the history range.
        // Seller ownership must not disappear when a request is old or modified.
        var linked = await visibleRequests
            .Where(request => request.RateId.HasValue)
            .OrderByDescending(request => request.RequestedAtUtc)
            .Select(request => new {
                request.RateId, request.SellerUserId, request.SellerName, request.RequestedAtUtc
            })
            .ToListAsync(ct);

        var latestOwner = linked.GroupBy(request => request.RateId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var directAssignedIds = new HashSet<Guid>();
        if (visibility.Mode != SellerVisibilityMode.All
            && visibilityService.HasScope(context.User, PricingConstants.Scopes.RateRequestCreate))
        {
            // Pricing-created rates can be assigned directly to the authenticated seller
            // without a request. Reuse the same Auth-based check as /seller-rates.
            try
            {
                var assigned = await sellerRateAccess.AssignedRateIdsAsync(currentUserId.Value, ct);
                directAssignedIds.UnionWith(assigned);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Linked seller-owned rates remain available during an Auth outage.
            }
        }

        var pendingQuery = db.RateHeaders.AsNoTracking().Where(rate => !rate.IsDeleted
            && (rate.Status == RateStatus.Sent
                || rate.Status == RateStatus.RequestedByClient
                || rate.Status == RateStatus.Expired));

        if (visibility.Mode != SellerVisibilityMode.All)
        {
            var allowedIds = latestOwner.Keys.Concat(directAssignedIds).Distinct().ToArray();
            pendingQuery = pendingQuery.Where(rate => allowedIds.Contains(rate.Id));
        }

        var pendingRates = await pendingQuery.OrderByDescending(rate => rate.CreatedAtUtc)
            .Select(rate => new {
                rate.Id, rate.RateCode, rate.QuoNumber, rate.ClientName,
                rate.ExecutiveName, rate.ShipmentMode, rate.Status,
                rate.CreatedAtUtc, rate.ValidTo, rate.SellerReceivedAtUtc
            })
            .ToListAsync(ct);

        // A selected-seller filter must also constrain directly created rates. They have
        // no SellerUserId, so never guess an identity from a display-name match.
        var pending = pendingRates
            .Where(rate => !sellerId.HasValue
                || (latestOwner.TryGetValue(rate.Id, out var owner) && owner.SellerUserId == sellerId)
                || (sellerId == currentUserId && directAssignedIds.Contains(rate.Id)
                    && !latestOwner.ContainsKey(rate.Id)))
            .Where(rate => string.IsNullOrWhiteSpace(mode)
                || rate.ShipmentMode.ToString().Equals(mode.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(rate => {
                latestOwner.TryGetValue(rate.Id, out var owner);
                return new {
                    rateId = rate.Id,
                    quoNumber = rate.QuoNumber ?? rate.RateCode,
                    clientName = rate.ClientName ?? "",
                    sellerUserId = owner?.SellerUserId
                        ?? (directAssignedIds.Contains(rate.Id) ? currentUserId : null),
                    sellerName = owner?.SellerName ?? rate.ExecutiveName ?? "Sin vendedor asignado",
                    shipmentMode = rate.ShipmentMode.ToString(),
                    status = rate.Status.ToString(),
                    pendingStage = rate.Status == RateStatus.Expired ? "Expired"
                        : rate.SellerReceivedAtUtc.HasValue ? "AwaitingClient"
                        : "AwaitingReceipt",
                    createdAtUtc = rate.CreatedAtUtc,
                    validToUtc = rate.ValidTo,
                    sellerReceivedAtUtc = rate.SellerReceivedAtUtc
                };
            })
            .ToArray();

        return Results.Ok(new {
            generatedAtUtc = DateTime.UtcNow,
            scope = visibility.Mode.ToString(),
            requests = requests.Select(request => new {
                id = request.Id,
                rateId = request.RateId,
                status = request.Status.ToString(),
                priority = request.Priority.ToString(),
                isModification = RateModificationRequestPayload.IsModification(request.PayloadJson),
                requestedAtUtc = request.RequestedAtUtc,
                dueAtUtc = request.DueAtUtc,
                completedAtUtc = request.CompletedAtUtc,
                sellerUserId = request.SellerUserId,
                sellerName = request.SellerName ?? "",
                clientName = request.ClientName ?? "",
                shipmentMode = request.ShipmentMode ?? "",
                originName = request.OriginName ?? "",
                destinationName = request.PodName ?? request.DestinationName ?? "",
                quoNumber = request.RateId.HasValue
                    && quoteNumbers.TryGetValue(request.RateId.Value, out var quoNumber)
                    ? quoNumber : null
            }).ToArray(),
            pendingSellerRates = pending
        });
    }
}
