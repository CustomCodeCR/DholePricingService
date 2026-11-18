using System.Security.Claims;
using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateEndpoints
{
    public static IEndpointRouteBuilder MapSellerRateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/seller-rates", GetMineAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/{rateId:guid}", GetOneAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/{rateId:guid}/document", DownloadAsync)
            .WithTags("Seller rates")
            .RequireAuthorization()
            .RequireScope(PricingConstants.Scopes.RateRequestCreate);

        app.MapGet("/api/pricing/seller-rates/dashboard", GetAdminDashboardAsync)
            .WithTags("Seller dashboards")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetMineAsync(
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        RateCreatorIdentityService creatorIdentityService,
        SellerRateAccessService sellerAccess,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var currentUserId = httpContext.GetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value == Guid.Empty)
            return Results.Unauthorized();

        var linkedIds = await db.RateRequests
            .AsNoTracking()
            .Where(x => x.SellerUserId == currentUserId.Value && x.RateId.HasValue)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => x.RateId!.Value)
            .ToListAsync(cancellationToken);

        // A receipt is a persisted, user-ID-based assignment. Preserve visibility
        // even if the executive display name changes or Auth is unavailable.
        var receivedIds = await db.RateHeaders
            .AsNoTracking()
            .Where(rate => !rate.IsDeleted && rate.SellerReceivedBy == currentUserId.Value)
            .Select(rate => rate.Id)
            .ToArrayAsync(cancellationToken);

        // Rates issued directly by Pricing may not have a linked RateRequest.
        // Auth is only needed to resolve those name-based assignments: an Auth
        // outage must never hide requests or receipts already linked by user ID.
        IReadOnlyCollection<Guid> assignedIds;
        try
        {
            assignedIds = await sellerAccess.AssignedRateIdsAsync(currentUserId.Value, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            loggerFactory.CreateLogger("SellerRateEndpoints").LogWarning(error,
                "No se pudieron resolver los ejecutivos de Auth para el vendedor {UserId}.",
                currentUserId.Value);

            if (linkedIds.Count == 0 && receivedIds.Length == 0)
            {
                return Results.Problem(
                    detail: "No se pudieron verificar las tarifas asignadas. Intente nuevamente.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            assignedIds = Array.Empty<Guid>();
        }

        var orderedIds = linkedIds.Concat(receivedIds).Concat(assignedIds).Distinct().ToArray();
        if (orderedIds.Length == 0)
            return Results.Ok(Array.Empty<RateDto>());

        // A linked/assigned child grants access to its group, but each sibling
        // is separately checked against the seller's authorization before use.
        var allIds = orderedIds.ToList();
        var siblingIds = await db.Database.SqlQuery<Guid>($"""
            SELECT DISTINCT sibling.rate_id AS "Value"
            FROM pricing.rate_quote_alternatives sibling
            WHERE sibling.parent_rate_id IN (
                SELECT linked.parent_rate_id
                FROM pricing.rate_quote_alternatives linked
                WHERE linked.rate_id = ANY({orderedIds}))
            """).ToListAsync(cancellationToken);
        foreach (var siblingId in siblingIds.Except(allIds).ToArray())
        {
            var member = await db.RateHeaders.AsNoTracking()
                .FirstOrDefaultAsync(rate => rate.Id == siblingId && !rate.IsDeleted, cancellationToken);
            if (member is not null &&
                await sellerAccess.CanReadAsync(member, currentUserId.Value, cancellationToken))
                allIds.Add(siblingId);
        }

        var items = new List<RateDto>(allIds.Count);
        foreach (var rateId in allIds)
        {
            var result = await dispatcher.DispatchAsync(
                new GetRateByIdQuery(rateId),
                cancellationToken
            );

            if (result.IsSuccess && result.Value is not null)
                items.Add(result.Value);
        }

        var enrichedItems = await creatorIdentityService.EnrichAsync(
            items,
            cancellationToken
        );

        // Parent or chosen child represents the entire QUO. This happens
        // before the seller dashboard counts/ranks accepted vs rejected.
        var visibleIds = enrichedItems.Select(item => item.Id).ToArray();
        var groupData = await db.Database.SqlQuery<string>($"""
            SELECT a.rate_id::text || '|' || a.parent_rate_id::text || '|' ||
                   a.base_quote || '|' || COALESCE(chosen.rate_id::text, '') ||
                   '|' || totals.total::text AS "Value"
            FROM pricing.rate_quote_alternatives a
            LEFT JOIN pricing.rate_quote_alternatives chosen
              ON chosen.parent_rate_id = a.parent_rate_id AND chosen.status = 'Selected'
            JOIN (
                SELECT parent_rate_id, COUNT(*) AS total
                FROM pricing.rate_quote_alternatives GROUP BY parent_rate_id
            ) totals ON totals.parent_rate_id = a.parent_rate_id
            WHERE a.rate_id = ANY({visibleIds})
            """).ToListAsync(cancellationToken);
        var membership = new Dictionary<Guid, (Guid Parent, string BaseQuote, Guid? Selected, int Count)>();
        foreach (var row in groupData)
        {
            var fields = row.Split('|', 5);
            if (fields.Length != 5 || !Guid.TryParse(fields[0], out var id)
                || !Guid.TryParse(fields[1], out var parent)
                || !int.TryParse(fields[4], out var count)) continue;
            membership[id] = (parent, fields[2],
                Guid.TryParse(fields[3], out var selected) ? selected : null, count);
        }
        var representatives = enrichedItems.GroupBy(item =>
            membership.TryGetValue(item.Id, out var group) ? group.Parent : item.Id)
            .Select(group =>
            {
                var info = group.Select(item => membership.TryGetValue(item.Id, out var m)
                    ? m : ((Guid Parent, string BaseQuote, Guid? Selected, int Count)?)null)
                    .FirstOrDefault(value => value.HasValue);
                if (!info.HasValue) return group.First();
                var member = info.Value;
                var chosen = group.FirstOrDefault(item => item.Id == (member.Selected ?? member.Parent))
                    ?? group.First();
                var prefix = chosen.RateName.StartsWith(chosen.RateCode, StringComparison.OrdinalIgnoreCase)
                    ? chosen.RateCode : chosen.QuoNumber ?? chosen.RateCode;
                var commercialName = chosen.RateName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? member.BaseQuote + chosen.RateName[prefix.Length..] : chosen.RateName;
                return chosen with { QuoNumber = member.BaseQuote,
                    GroupQuoteNumber = member.BaseQuote, GroupAlternativeCount = member.Count,
                    RateName = commercialName };
            }).ToArray();

        var visibleItems = new List<object>(representatives.Length);
        foreach (var item in representatives)
        {
            var published = await ToSellerViewAsync(item, db, cancellationToken);
            if (published is not null)
                visibleItems.Add(published);
        }

        return Results.Ok(visibleItems.ToArray());
    }
    private static async Task<IResult> GetOneAsync(
        Guid rateId,
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        SellerRateAccessService sellerAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, userId.Value, cancellationToken)) return Results.Forbid();

        // Old child URLs still resolve to the chosen option of the same QUO.
        var snapshot = await db.Database.SqlQuery<string>($"""
            SELECT a.base_quote || '|' || totals.total::text || '|' ||
                   COALESCE(chosen.rate_id::text, '') AS "Value"
            FROM pricing.rate_quote_alternatives a
            JOIN (SELECT parent_rate_id, COUNT(*) AS total
                  FROM pricing.rate_quote_alternatives GROUP BY parent_rate_id)
              totals ON totals.parent_rate_id = a.parent_rate_id
            LEFT JOIN pricing.rate_quote_alternatives chosen
              ON chosen.parent_rate_id = a.parent_rate_id AND chosen.status = 'Selected'
            WHERE a.rate_id = {rateId}
            """).FirstOrDefaultAsync(cancellationToken);
        var groupData = snapshot?.Split('|', 3);
        if (groupData is { Length: 3 } &&
            Guid.TryParse(groupData[2], out var selectedId) && selectedId != rateId)
        {
            var selected = await db.RateHeaders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == selectedId && !x.IsDeleted, cancellationToken);
            if (selected is not null &&
                await sellerAccess.CanReadAsync(selected, userId.Value, cancellationToken))
                rateId = selectedId;
        }
        var result = await dispatcher.DispatchAsync(new GetRateByIdQuery(rateId), cancellationToken);
        if (!result.IsSuccess || result.Value is null) return Results.NotFound();
        var commercial = result.Value;
        if (groupData is { Length: 3 } && int.TryParse(groupData[1], out var count))
        {
            var code = commercial.RateName.StartsWith(commercial.RateCode, StringComparison.OrdinalIgnoreCase)
                ? commercial.RateCode : commercial.QuoNumber ?? commercial.RateCode;
            var name = commercial.RateName.StartsWith(code, StringComparison.OrdinalIgnoreCase)
                ? groupData[0] + commercial.RateName[code.Length..] : commercial.RateName;
            commercial = commercial with { QuoNumber = groupData[0], GroupQuoteNumber = groupData[0],
                GroupAlternativeCount = count, RateName = name };
        }
        var visible = await ToSellerViewAsync(commercial, db, cancellationToken);
        return visible is not null ? Results.Ok(visible) : Results.NotFound();
    }

    private static async Task<IResult> DownloadAsync(
        Guid rateId,
        ServiceDbContext db,
        ICommandDispatcher dispatcher,
        SellerRateAccessService sellerAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty) return Results.Unauthorized();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
        if (rate is null) return Results.NotFound();
        if (!await sellerAccess.CanReadAsync(rate, userId.Value, cancellationToken)) return Results.Forbid();

        // Generating the PDF for a revision still being priced would expose an
        // unpublished offer. The prior issued PDF remains in the seller's email.
        if (IsPricingDraft(rate.Status.ToString()))
            return Results.Conflict(new {
                message = "Pricing está preparando una nueva revisión. La última cotización enviada sigue disponible en su correo."
            });

        var result = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(rateId, null, "pdf"), cancellationToken);
        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : Results.Problem("No fue posible generar el PDF de la cotización.");
    }

    // Sólo el SuperUsuario o quien tiene permiso view-all puede consultar este resumen.
    // Se proyectan datos de cabecera, sin cargar cada tarifa por separado.
    private static async Task<IResult> GetAdminDashboardAsync(
        ServiceDbContext db,
        SellerVisibilityService visibility,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IsAdminViewer(httpContext.User, visibility))
            return Results.Forbid();

        // Global supervisory dashboard: include manual and other quotations
        // even when they were not created from a RateRequest.
        var rows = await db.RateHeaders
            .AsNoTracking()
            .Where(rate => !rate.IsDeleted &&
                !(rate.RateType == Dhole.Pricing.Domain.Rates.Enums.RateType.Tariff &&
                  rate.ClientName != null && rate.ClientName.ToLower().Contains("tarifario")))
            .OrderByDescending(rate => rate.CreatedAtUtc)
            .Select(rate => new
            {
                rate.Id,
                rate.RateCode,
                rate.QuoNumber,
                rate.IdtraNumber,
                rate.ClientName,
                rate.ExecutiveName,
                rate.PolName,
                rate.PoeName,
                rate.PodName,
                rate.ContainerQuantity,
                rate.ContainerTypeName,
                rate.ShipmentMode,
                rate.ValidFrom,
                rate.ValidTo,
                rate.TotalSaleAmount,
                rate.CurrencyName,
                rate.CurrencyCode,
                rate.Status,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(rows.Select(rate => new
        {
            rate.Id,
            rate.RateCode,
            rate.QuoNumber,
            rate.IdtraNumber,
            rate.ClientName,
            rate.ExecutiveName,
            rate.PolName,
            rate.PoeName,
            rate.PodName,
            rate.ContainerQuantity,
            rate.ContainerTypeName,
            shipmentMode = rate.ShipmentMode.ToString(),
            rate.ValidFrom,
            rate.ValidTo,
            rate.TotalSaleAmount,
            rate.CurrencyName,
            rate.CurrencyCode,
            status = rate.Status.ToString(),
        }));
    }


    private static bool IsPricingDraft(string status) => status is
        "Open" or "PendingApproval" or "ApprovedByManagement" or "RejectedByManagement";

    private static async Task<object?> ToSellerViewAsync(
        RateDto rate, ServiceDbContext db, CancellationToken cancellationToken)
    {
        if (!IsPricingDraft(rate.Status))
            return ToSellerView(rate);

        // A new draft replaces the live header while its previous published
        // revision is stored in RateRevisions. Never disclose the new draft's
        // commercial terms to Sales until Pricing explicitly resends it.
        var published = await db.Set<RateRevision>().AsNoTracking()
            .Where(revision => revision.RateHeaderId == rate.Id
                && (revision.Status == "Sent"
                    || revision.Status == "RequestedByClient"
                    || revision.Status == "AcceptedByClient"
                    || revision.Status == "RejectedByClient"
                    || revision.Status == "Expired"))
            .OrderByDescending(revision => revision.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (published is null) return null; // Never reveal a quote never issued.
        // An unpublished draft is not synonymous with an open Pricing request.
        // Keep the issued revision private from draft changes, but expose the
        // actual work-queue state separately for accurate seller dashboards.
        var openModificationPayloads = await db.RateRequests.AsNoTracking()
            .Where(request => request.RateId == rate.Id
                && request.Status == RateRequestStatus.Open)
            .Select(request => request.PayloadJson)
            .ToListAsync(cancellationToken);
        var pricingModificationActive = openModificationPayloads
            .Any(RateModificationRequestPayload.IsModification);

        return ToHistoricalSellerView(rate, published, pricingModificationActive);
    }

    private sealed record SellerPublishedRateDetail(
        Guid Id, string Name, decimal Quantity, string? CurrencyCode,
        decimal SaleAmount, bool BillToClient);

    private static object ToHistoricalSellerView(RateDto current, RateRevision published, bool pricingModificationActive)
    {
        using var document = JsonDocument.Parse(published.SnapshotJson);
        var previous = document.RootElement;

        static JsonElement Field(JsonElement json, string name)
            => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value)
                ? value : default;
        static string? Text(JsonElement json, string name)
        {
            var value = Field(json, name);
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        static decimal Amount(JsonElement json, string name)
        {
            var value = Field(json, name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount)
                ? amount : 0m;
        }
        static int Number(JsonElement json, string name)
        {
            var value = Field(json, name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var amount)
                ? amount : 0;
        }
        static DateTime Date(JsonElement json, string name, DateTime fallback)
        {
            var value = Field(json, name);
            return value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var parsed)
                ? parsed : fallback;
        }

        var modeValue = Field(previous, "ShipmentMode");
        var shipmentMode = modeValue.ValueKind == JsonValueKind.Number && modeValue.TryGetInt32(out var modeCode)
            ? ((ShipmentMode)modeCode).ToString()
            : modeValue.ValueKind == JsonValueKind.String ? modeValue.GetString() ?? current.ShipmentMode
            : current.ShipmentMode;

        var detailsValue = Field(previous, "Details");
        var details = detailsValue.ValueKind == JsonValueKind.Array
            ? detailsValue.EnumerateArray()
                .Where(item => Amount(item, "SaleAmount") > 0m)
                .Select(item => new SellerPublishedRateDetail(
                    Guid.TryParse(Text(item, "Id"), out var detailId) ? detailId : Guid.Empty,
                    Text(item, "Name") ?? "",
                    Amount(item, "Quantity"),
                    Text(item, "CurrencyCode"),
                    Amount(item, "SaleAmount"),
                    true
                )).ToArray()
            : Array.Empty<SellerPublishedRateDetail>();

        var historicalName = published.RateName;
        if (current.GroupQuoteNumber is not null &&
            !string.IsNullOrWhiteSpace(published.QuoNumber) &&
            historicalName.StartsWith(published.QuoNumber, StringComparison.OrdinalIgnoreCase))
            historicalName = current.GroupQuoteNumber + historicalName[published.QuoNumber.Length..];

        return new
        {
            current.Id,
            RateCode = Text(previous, "RateCode") ?? current.RateCode,
            RateName = historicalName,
            RevisionNumber = published.RevisionNumber,
            QuoNumber = current.GroupQuoteNumber ?? published.QuoNumber,
            current.GroupQuoteNumber,
            current.GroupAlternativeCount,
            IdtraNumber = published.IdtraNumber,
            ClientName = Text(previous, "ClientName"),
            ExecutiveName = Text(previous, "ExecutiveName"),
            Status = published.Status,
            // The saved quotation is still protected, but its live workflow status
            // is safe to show independently. An Open header is not necessarily
            // an Open seller modification request.
            CurrentRateStatus = current.Status,
            PendingReapproval = current.PendingReapproval,
            ReapprovalReason = current.ReapprovalReason,
            SellerReceivedAtUtc = (DateTime?)null,
            SellerReceivedBy = (Guid?)null,
            ShipmentMode = shipmentMode,
            ContainerQuantity = Number(previous, "ContainerQuantity"),
            ContainerTypeName = Text(previous, "ContainerTypeName"),
            PolName = Text(previous, "PolName"),
            PoeName = Text(previous, "PoeName"),
            PodName = Text(previous, "PodName"),
            CurrencyName = Text(previous, "CurrencyName"),
            CurrencyCode = Text(previous, "CurrencyCode"),
            ValidFrom = Date(previous, "ValidFrom", current.ValidFrom),
            ValidTo = Date(previous, "ValidTo", current.ValidTo),
            TotalSaleAmount = Amount(previous, "TotalSaleAmount"),
            Includes = Text(previous, "Includes"),
            SubjectTo = Text(previous, "SubjectTo"),
            Excludes = Text(previous, "Excludes"),
            TransitTime = Text(previous, "TransitTime"),
            RateDetails = details,
            RevisionPending = true,
            PricingModificationActive = pricingModificationActive,
        };
    }

    // Never expose Pricing costs, margins or internal supplier data to a seller.
    // Seller API returns only the commercial information found in their quotation.
    private static object ToSellerView(RateDto rate) => new
    {
        rate.Id,
        rate.RateCode,
        rate.RateName,
        rate.RevisionNumber,
        rate.QuoNumber,
        rate.GroupQuoteNumber,
        rate.GroupAlternativeCount,
        rate.IdtraNumber,
        rate.ClientName,
        rate.ExecutiveName,
        rate.Status,
        CurrentRateStatus = rate.Status,
        rate.PendingReapproval,
        rate.ReapprovalReason,
        rate.SellerReceivedAtUtc,
        rate.SellerReceivedBy,
        rate.ShipmentMode,
        rate.ContainerQuantity,
        rate.ContainerTypeName,
        rate.PolName,
        rate.PoeName,
        rate.PodName,
        rate.CurrencyName,
        rate.CurrencyCode,
        rate.ValidFrom,
        rate.ValidTo,
        rate.TotalSaleAmount,
        rate.Includes,
        rate.SubjectTo,
        rate.Excludes,
        rate.TransitTime,
        RevisionPending = false,
        PricingModificationActive = false,
        RateDetails = rate.RateDetails
            .Where(detail => detail.SaleAmount > 0m)
            .Select(detail => new
            {
                detail.Id,
                detail.Name,
                detail.Quantity,
                detail.CurrencyCode,
                detail.SaleAmount,
                detail.BillToClient,
            }).ToArray(),
    };

    internal static bool IsAdminViewer(ClaimsPrincipal principal, SellerVisibilityService visibility)
    {
        if (visibility.HasScope(principal, PricingConstants.Scopes.RateRequestViewAll))
            return true;

        return principal.Claims
            .Where(claim => claim.Type == ClaimTypes.Role
                || claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase)
                || claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split(
                new[] { ' ', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(role => role.Equals("SuperUsuario", StringComparison.OrdinalIgnoreCase)
                || role.Equals("SuperUser", StringComparison.OrdinalIgnoreCase));
    }

}
