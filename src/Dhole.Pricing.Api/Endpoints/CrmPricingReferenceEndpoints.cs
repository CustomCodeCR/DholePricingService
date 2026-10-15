using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>Read-only, authorized commercial projections for CRM.
/// Never copies costs or exposes arbitrary seller records.</summary>
public static class CrmPricingReferenceEndpoints
{
    public static IEndpointRouteBuilder MapCrmPricingReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/crm")
            .WithTags("CRM Pricing references")
            .RequireAuthorization();
        group.MapGet("/requests/{id:guid}", GetRequest)
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);
        group.MapGet("/quotes/{id:guid}", GetQuote)
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetRequest(Guid id, ServiceDbContext db,
        SellerVisibilityService visibility, HttpContext context, CancellationToken ct)
    {
        var rights = await visibility.ResolveAsync(context, requireElevated: false, ct);
        if (rights is null) return Results.Forbid();
        var item = await db.RateRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (rights.Mode != SellerVisibilityMode.All &&
            (item.SellerUserId is null || !rights.SellerUserIds.Contains(item.SellerUserId.Value)))
            return Results.Forbid();
        return Results.Ok(new {
            id = item.Id,
            sellerUserId = item.SellerUserId,
            status = item.Status.ToString(),
            rateId = item.RateId,
            clientName = item.ClientName,
            shipmentMode = item.ShipmentMode,
            requestedAtUtc = item.RequestedAtUtc
        });
    }

    private static async Task<IResult> GetQuote(Guid id, ServiceDbContext db,
        SellerVisibilityService visibility, HttpContext context, CancellationToken ct)
    {
        // Sellers may have request-create without global rate-view; visibility is still mandatory.
        if (!visibility.HasScope(context.User, PricingConstants.Scopes.RateView) &&
            !visibility.HasScope(context.User, PricingConstants.Scopes.RateRequestCreate))
            return Results.Forbid();
        // Restrict to rates legitimately visible to this seller, never all quotes.
        if (!await visibility.CanViewRateAsync(context,id,ct))
            return Results.Forbid();
        var quote = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (quote is null) return Results.NotFound();
        var request = await db.RateRequests.AsNoTracking()
            .Where(x => x.RateId == id)
            .OrderByDescending(x=>x.RequestedAtUtc)
            .Select(x => new { x.Id,x.SellerUserId })
            .FirstOrDefaultAsync(ct);
        return Results.Ok(new {
            id = quote.Id,
            quoteNumber = quote.QuoNumber ?? quote.RateCode,
            rateCode = quote.RateCode,
            status = quote.Status.ToString(),
            shipmentMode = quote.ShipmentMode.ToString(),
            requestId = request == null ? (Guid?)null : request.Id,
            sellerUserId = request == null ? (Guid?)null : request.SellerUserId
        });
    }
}
