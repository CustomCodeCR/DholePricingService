using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class LclRateSourceEndpoints
{
    public static IEndpointRouteBuilder MapLclRateSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/pricing/lcl-rate-sources")
            .WithTags("LCL Rate Sources")
            .RequireAuthorization();

        group
            .MapGet("/coloaders", BrowseColoaderTariffsAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        return app;
    }

    private static async Task<IResult> BrowseColoaderTariffsAsync(
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? incotermId,
        string? pol,
        string? poe,
        string? pod,
        DateTime? quoteDate,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var effectiveDate = (quoteDate ?? DateTime.UtcNow).Date;

        // Keep the same selection philosophy used by the approved FCL picker:
        // approved/open tariffs + requested loading date first, then resolve the
        // route with catalog ids OR the stored textual snapshots. This matters
        // when a coloader tariff was approved with an older catalog snapshot.
        var candidates = await db.RateHeaders
            .AsNoTracking()
            .Where(rate =>
                rate.ShipmentMode == ShipmentMode.Lcl
                && rate.RateType == RateType.Tariff
                && rate.ValidFrom <= effectiveDate
                && rate.ValidTo >= effectiveDate
                && (rate.Status == RateStatus.Open || rate.Status == RateStatus.ApprovedByManagement))
            .OrderBy(rate => rate.ValidTo)
            .ThenBy(rate => rate.TotalSaleAmount)
            .Take(250)
            .Select(rate => new
            {
                rate.Id,
                rate.RateCode,
                rate.RateName,
                rate.AgentId,
                rate.AgentName,
                rate.AgentCode,
                rate.CarrierId,
                rate.CarrierName,
                rate.CarrierCode,
                rate.PolId,
                rate.PolName,
                rate.PolCode,
                rate.PoeId,
                rate.PoeName,
                rate.PoeCode,
                rate.PodId,
                rate.PodName,
                rate.PodCode,
                rate.IncotermId,
                rate.IncotermName,
                rate.IncotermCode,
                rate.CurrencyId,
                rate.CurrencyName,
                rate.CurrencyCode,
                rate.FreeDays,
                rate.TransitTime,
                rate.ValidFrom,
                rate.ValidTo,
                rate.ChargeableQuantity,
                rate.TotalCostAmount,
                rate.TotalSaleAmount,
                rate.TotalUtilityAmount,
                rate.MarginPercentage,
                rate.Includes,
                rate.SubjectTo,
                rate.Excludes,
                rate.Status,
            })
            .ToListAsync(cancellationToken);

        var routeMatches = candidates
            .Where(header => LocationMatches(polId, pol, header.PolId, header.PolName, header.PolCode))
            .Where(header => LocationMatches(poeId, poe, header.PoeId, header.PoeName, header.PoeCode))
            .Where(header => PodMatchesOrIsUnassigned(podId, pod, header.PodId, header.PodName, header.PodCode))
            .ToList();

        // Incoterm is a soft filter for coloaders: prefer the same Incoterm (or a
        // generic tariff with no Incoterm). If none exists, keep the same route
        // available just like the FCL pre-approved fallback does.
        var incotermMatches = incotermId.HasValue
            ? routeMatches.Where(header => header.IncotermId == incotermId || header.IncotermId == null).ToList()
            : routeMatches;

        var headers = (incotermMatches.Count > 0 ? incotermMatches : routeMatches)
            .Take(100)
            .ToList();

        var ids = headers.Select(header => header.Id).ToArray();
        List<ColoaderLine> details;

        if (ids.Length == 0)
        {
            details = [];
        }
        else
        {
            details = await db.RateDetails
                .AsNoTracking()
                .Where(detail => ids.Contains(detail.RateHeaderId))
                .OrderBy(detail => detail.Name)
                .Select(detail => new ColoaderLine(
                    detail.RateHeaderId,
                    detail.Id,
                    detail.CostId,
                    detail.Name,
                    detail.CostDetailType.ToString(),
                    detail.CostType.ToString(),
                    detail.ChargeBasis.ToString(),
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Quantity,
                    detail.UtilityAmount,
                    detail.Notes,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate))
                .ToListAsync(cancellationToken);
        }

        var linesByRate = details
            .GroupBy(detail => detail.RateHeaderId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var items = headers.Select(header =>
        {
            var lines = linesByRate.TryGetValue(header.Id, out var rateLines)
                ? rateLines
                : Array.Empty<ColoaderLine>();

            return new
            {
                sourceType = "Coloader",
                id = header.Id,
                header.RateCode,
                header.RateName,
                providerId = header.AgentId,
                providerName = header.AgentName,
                providerCode = header.AgentCode,
                header.CarrierId,
                header.CarrierName,
                header.CarrierCode,
                header.PolId,
                header.PolName,
                header.PolCode,
                header.PoeId,
                header.PoeName,
                header.PoeCode,
                header.PodId,
                header.PodName,
                header.PodCode,
                header.IncotermId,
                header.IncotermName,
                header.IncotermCode,
                header.CurrencyId,
                header.CurrencyName,
                header.CurrencyCode,
                header.FreeDays,
                header.TransitTime,
                header.ValidFrom,
                header.ValidTo,
                header.ChargeableQuantity,
                header.TotalCostAmount,
                header.TotalSaleAmount,
                header.TotalUtilityAmount,
                header.MarginPercentage,
                header.Includes,
                header.SubjectTo,
                header.Excludes,
                status = header.Status.ToString(),
                lines,
            };
        });

        return Results.Ok(new { items });
    }

    private static bool LocationMatches(
        Guid? requestedId,
        string? requestedText,
        Guid candidateId,
        string candidateName,
        string candidateCode)
    {
        if (!requestedId.HasValue && string.IsNullOrWhiteSpace(requestedText)) return true;
        if (requestedId.HasValue && requestedId.Value == candidateId) return true;
        if (string.IsNullOrWhiteSpace(requestedText)) return false;

        var requested = CanonicalText(requestedText);
        var name = CanonicalText(candidateName);
        var code = CanonicalText(candidateCode);
        if (string.IsNullOrEmpty(requested)) return false;

        return (!string.IsNullOrEmpty(name)
                && (requested.Contains(name, StringComparison.Ordinal)
                    || name.Contains(requested, StringComparison.Ordinal)))
            || (!string.IsNullOrEmpty(code)
                && (requested.Contains(code, StringComparison.Ordinal)
                    || code.Contains(requested, StringComparison.Ordinal)));
    }

    private static bool PodMatchesOrIsUnassigned(
        Guid? requestedId,
        string? requestedText,
        Guid? candidateId,
        string? candidateName,
        string? candidateCode)
    {
        if (!requestedId.HasValue && string.IsNullOrWhiteSpace(requestedText)) return true;
        if (!candidateId.HasValue && string.IsNullOrWhiteSpace(candidateName) && string.IsNullOrWhiteSpace(candidateCode)) return true;
        if (requestedId.HasValue && candidateId.HasValue && requestedId.Value == candidateId.Value) return true;
        if (string.IsNullOrWhiteSpace(requestedText)) return false;

        var requested = CanonicalText(requestedText);
        var name = CanonicalText(candidateName);
        var code = CanonicalText(candidateCode);
        if (string.IsNullOrEmpty(name) || name is "porasignar" or "unassigned" or "pending") return true;

        return requested.Contains(name, StringComparison.Ordinal)
            || name.Contains(requested, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(code)
                && (requested.Contains(code, StringComparison.Ordinal)
                    || code.Contains(requested, StringComparison.Ordinal)));
    }

    private static string CanonicalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(normalized);
    }

    private sealed record ColoaderLine(
        Guid RateHeaderId,
        Guid Id,
        Guid? CostId,
        string Name,
        string CostDetailType,
        string CostType,
        string ChargeBasis,
        Guid CurrencyId,
        string CurrencyName,
        string CurrencyCode,
        decimal CostAmount,
        decimal SaleAmount,
        decimal Quantity,
        decimal UtilityAmount,
        string? Notes,
        bool ApplyDestinationTax,
        decimal DestinationTaxRate);
}
