using System.Text.RegularExpressions;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Retrieves explicit own-LCL quotation references. Never infer a customer
/// quotation from carrier, port or ETA because departures can overlap.
/// </summary>
internal static class OwnLclLinkedQuoteLookup
{
    private static readonly Regex IdMarker = new(
        @"ConsolidadoId:\s*(?<id>[0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberMarker = new(
        @"Consolidado:\s*#(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<IReadOnlyList<OwnLclLinkedQuoteDto>> LoadAsync(
        ServiceDbContext db, Guid consolidationId, int consolidationNumber,
        CancellationToken ct)
    {
        var idLike = $"%{consolidationId:D}%";
        var numberLike = $"%Consolidado:%#{consolidationNumber}%";

        // Search candidate notes in PostgreSQL; verify the full marker in .NET.
        // Otherwise consolidation #5 could erroneously match #55.
        var candidates = await db.RateHeaders.AsNoTracking()
            .Include(rate => rate.RateDetails)
            .AsSplitQuery()
            .Where(rate => !rate.IsDeleted
                && rate.ShipmentMode == ShipmentMode.Lcl
                && (rate.QuoNumber != null || rate.RateCode.StartsWith("QUO-"))
                && rate.RateDetails.Any(detail => detail.Notes != null
                    && (EF.Functions.ILike(detail.Notes, idLike)
                        || EF.Functions.ILike(detail.Notes, numberLike))))
            .ToListAsync(ct);

        return candidates
            .Where(rate => IsLinkedTo(rate, consolidationId, consolidationNumber))
            .OrderByDescending(rate => rate.CreatedAtUtc)
            .ThenByDescending(rate => rate.Id)
            .Select(rate => new OwnLclLinkedQuoteDto(
                rate.Id,
                string.IsNullOrWhiteSpace(rate.QuoNumber) ? rate.RateCode : rate.QuoNumber,
                rate.ClientName,
                rate.Status.ToString(),
                rate.CreatedAtUtc,
                Math.Max(0m, rate.TotalVolumeCbm),
                ResolveBillableCbm(rate)))
            .ToArray();
    }

    // The physical CBM can be fractional. The billable CBM follows the same
    // freight-quantity-first policy used by own-LCL capacity allocation.
    // Miami keeps its actual CFT-derived volume and has no artificial 1-CBM floor.
    internal static decimal ResolveBillableCbm(RateHeader rate)
    {
        var freightQuantity = rate.RateDetails
            .Where(detail => detail.CostDetailType == CostDetailType.Freight
                && detail.ChargeBasis is ChargeBasis.PerCbm or ChargeBasis.PerChargeableCbm)
            .Select(detail => detail.Quantity)
            .DefaultIfEmpty(0m)
            .Max();

        var cbm = freightQuantity > 0m
            ? freightQuantity
            : Math.Max(0m, rate.ChargeableQuantity);
        if (cbm <= 0m) return 0m;

        var isMiami = string.Equals(rate.PolCode?.Trim(), "MIA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rate.PolCode?.Trim(), "USMIA", StringComparison.OrdinalIgnoreCase)
            || (rate.PolName?.Contains("Miami", StringComparison.OrdinalIgnoreCase) ?? false);

        return isMiami ? cbm : Math.Max(1m, cbm);
    }

    internal static bool IsLinkedTo(RateHeader rate, Guid consolidationId, int consolidationNumber)
    {
        var notes = rate.RateDetails
            .Select(detail => detail.Notes)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => note!)
            .ToArray();

        // Prefer explicit GUID over an old numerical marker on any detail.
        var ids = new HashSet<Guid>();
        foreach (var note in notes)
            foreach (Match match in IdMarker.Matches(note))
                if (Guid.TryParse(match.Groups["id"].Value, out var parsed))
                    ids.Add(parsed);
        if (ids.Count > 0)
            return ids.Contains(consolidationId);

        return notes.Any(note => NumberMarker.Matches(note)
            .Cast<Match>()
            .Any(match => int.TryParse(match.Groups["number"].Value, out var number)
                && number == consolidationNumber));
    }
}

public sealed record OwnLclLinkedQuoteDto(
    Guid Id,
    string QuoNumber,
    string? ClientName,
    string Status,
    DateTime CreatedAtUtc,
    decimal PhysicalCbm,
    decimal BillableCbm);

