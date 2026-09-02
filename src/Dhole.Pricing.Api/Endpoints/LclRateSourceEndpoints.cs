using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Domain.Rates.Enums;
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
        DateTime? quoteDate,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var effectiveDate = (quoteDate ?? DateTime.UtcNow).Date;

        var query = db.RateHeaders
            .AsNoTracking()
            .Where(rate =>
                rate.ShipmentMode == ShipmentMode.Lcl
                && rate.RateType == RateType.Tariff
                && rate.ValidFrom <= effectiveDate
                && rate.ValidTo >= effectiveDate
                && (rate.Status == RateStatus.Open || rate.Status == RateStatus.ApprovedByManagement));

        if (polId.HasValue)
        {
            query = query.Where(rate => rate.PolId == polId.Value);
        }

        if (poeId.HasValue)
        {
            query = query.Where(rate => rate.PoeId == poeId.Value);
        }

        if (podId.HasValue)
        {
            query = query.Where(rate => rate.PodId == podId.Value);
        }

        if (incotermId.HasValue)
        {
            query = query.Where(rate => rate.IncotermId == incotermId.Value || rate.IncotermId == null);
        }

        var headers = await query
            .OrderBy(rate => rate.ValidTo)
            .ThenBy(rate => rate.TotalSaleAmount)
            .Take(100)
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
