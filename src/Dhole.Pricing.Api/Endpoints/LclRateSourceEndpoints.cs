using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
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
        IImportFclRateRepository importRates,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var effectiveDate = (quoteDate ?? DateTime.UtcNow).Date;

        // La fecha de carga funciona como límite inferior de vencimiento: se muestran
        // tarifas que venzan ese día o después, incluso si su vigencia inicia después.
        // Luego se resuelve la ruta con ids de catálogo o snapshots textuales.
        var candidates = await db.RateHeaders
            .AsNoTracking()
            .Where(rate =>
                rate.ShipmentMode == ShipmentMode.Lcl
                && rate.RateType == RateType.Tariff
                && rate.ValidTo >= effectiveDate
                && (rate.Status == RateStatus.Open || rate.Status == RateStatus.ApprovedByManagement))
            .OrderBy(rate => rate.ValidFrom)
            .ThenBy(rate => rate.ValidTo)
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

        // Imported LCL rows intentionally share the legacy ImportFclRates storage with
        // FCL for backward compatibility. They must nevertheless be exposed only as
        // LCL/coloader sources in Pantalla 5, never through the FCL selector.
        // Imported LCL rows intentionally share the legacy ImportFclRates storage with
        // FCL for backward compatibility. They must nevertheless be exposed only as
        // LCL/coloader sources in Pantalla 5, never through the FCL selector.
        var importedLclCandidates = await db.ImportFclRates
            .AsNoTracking()
            .Where(rate =>
                !rate.IsDeleted
                && (rate.Status == ImportStatus.Approved
                    || rate.Status == ImportStatus.PreAuthorized)
                && rate.ValidTo >= effectiveDate)
            .OrderBy(rate => rate.ValidFrom)
            .ThenBy(rate => rate.ValidTo)
            .ThenBy(rate => rate.TotalSale)
            .Take(500)
            .ToListAsync(cancellationToken);

        var directlyClassifiedLclRates = importedLclCandidates
            .Where(IsImportedLclRate)
            .Where(rate => LocationMatches(polId, pol, rate.PolId, rate.PolName, rate.PolCode))
            .Where(rate => LocationMatches(poeId, poe, rate.PoeId, rate.PoeName, rate.PoeCode))
            .Where(rate => PodMatchesOrIsUnassigned(
                podId,
                pod,
                rate.PodId,
                rate.PodName,
                rate.PodCode))
            .ToList();

        // Reuse the same imported-rate source queried by Pantalla 5 FCL. Historical
        // coloader rows were stored in ImportFclRates without an explicit LCL marker,
        // so they can still be returned by /api/pricing/import-rates/select while the
        // dedicated LCL endpoint would otherwise return an empty list.
        var selectorApproved = await importRates.GetForSelectAsync(
            status: ImportStatus.Approved,
            pol: pol,
            poe: poe,
            pod: pod,
            quoteDate: effectiveDate,
            cancellationToken: cancellationToken);
        var selectorPreAuthorized = await importRates.GetForSelectAsync(
            status: ImportStatus.PreAuthorized,
            pol: pol,
            poe: poe,
            pod: pod,
            quoteDate: effectiveDate,
            cancellationToken: cancellationToken);

        var selectorIds = selectorApproved
            .Concat(selectorPreAuthorized)
            .Select(rate => rate.Id)
            .Distinct()
            .ToArray();

        var selectorCandidates = selectorIds.Length == 0
            ? new List<ImportFclRates>()
            : await db.ImportFclRates
                .AsNoTracking()
                .Where(rate => selectorIds.Contains(rate.Id) && !rate.IsDeleted)
                .ToListAsync(cancellationToken);

        var importedLclRates = directlyClassifiedLclRates
            .Concat(selectorCandidates
                .Where(IsImportedLclRate)
                .Where(rate => rate.ValidTo >= effectiveDate)
                .Where(rate => LocationMatches(polId, pol, rate.PolId, rate.PolName, rate.PolCode))
                .Where(rate => LocationMatches(poeId, poe, rate.PoeId, rate.PoeName, rate.PoeCode))
                .Where(rate => PodMatchesOrIsUnassigned(
                    podId,
                    pod,
                    rate.PodId,
                    rate.PodName,
                    rate.PodCode)))
            .GroupBy(rate => rate.Id)
            .Select(group => group.First())
            .OrderBy(rate => rate.ValidFrom)
            .ThenBy(rate => rate.ValidTo)
            .ThenBy(rate => rate.TotalSale)
            .Take(100)
            .ToList();

        var tariffItems = headers.Select(header =>
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
        }).Cast<object>();

        var importedItems = importedLclRates.Select(rate =>
        {
            var totalCost = ResolveImportedLclTotalCost(rate);
            var totalSale = Math.Max(0m, rate.TotalSale ?? totalCost);
            var profit = totalSale - totalCost;
            var margin = totalSale > 0m
                ? decimal.Round((profit / totalSale) * 100m, 4)
                : 0m;
            var hasAssignedPod = !IsUnassignedLocation(rate.PodName, rate.PodCode, rate.PodSlug);
            var lines = BuildImportedLclLines(rate, totalCost, totalSale);

            return new
            {
                sourceType = "Coloader",
                id = rate.Id,
                rateCode = $"IMP-LCL-{rate.Id.ToString("N")[..8].ToUpperInvariant()}",
                rateName = $"{rate.AgentName} LCL · {rate.PolName} → {(hasAssignedPod ? rate.PodName : rate.PoeName)}",
                providerId = (Guid?)rate.AgentId,
                providerName = rate.AgentName,
                providerCode = rate.AgentCode,
                carrierId = (Guid?)rate.CarrierId,
                carrierName = rate.CarrierName,
                carrierCode = rate.CarrierCode,
                polId = rate.PolId,
                polName = rate.PolName,
                polCode = rate.PolCode,
                poeId = rate.PoeId,
                poeName = rate.PoeName,
                poeCode = rate.PoeCode,
                podId = hasAssignedPod ? rate.PodId : (Guid?)null,
                podName = hasAssignedPod ? rate.PodName : null,
                podCode = hasAssignedPod ? rate.PodCode : null,
                incotermId = (Guid?)null,
                incotermName = (string?)null,
                incotermCode = (string?)null,
                currencyId = rate.CurrencyId,
                currencyName = rate.CurrencyName,
                currencyCode = rate.CurrencyCode,
                freeDays = rate.FreeDays,
                transitTime = rate.TransitDays?.ToString(),
                validFrom = rate.ValidFrom,
                validTo = rate.ValidTo,
                chargeableQuantity = 1m,
                totalCostAmount = totalCost,
                totalSaleAmount = totalSale,
                totalUtilityAmount = profit,
                marginPercentage = margin,
                includes = (string?)null,
                subjectTo = (string?)null,
                excludes = (string?)null,
                status = rate.Status.ToString(),
                lines,
            };
        }).Cast<object>();

        var items = tariffItems.Concat(importedItems).ToArray();
        return Results.Ok(new { items });
    }

    private static bool IsImportedLclRate(ImportFclRates rate)
    {
        var markers = new[]
        {
            rate.ContainerType,
            rate.ContainerTypeName,
            rate.ContainerTypeCode,
            rate.ContainerTypeSlug,
            rate.ImportProfileName,
            rate.ImportProfileCode,
            rate.ImportProfileSlug,
            rate.Commodity,
            rate.SpaceComment,
            rate.RawDataJson,
        };

        if (markers.Any(ContainsLclMarker)) return true;

        // Legacy coloader imports often have neither naviera nor equipment resolved.
        // They were therefore falling through the old FCL selector simply because
        // they did not contain the literal string "LCL". A real FCL rate must expose
        // a recognizable full-container equipment; otherwise an unresolved equipment
        // or carrier snapshot is treated as an LCL/coloader candidate.
        if (HasExplicitFclEquipment(rate.ContainerTypeName, rate.ContainerTypeSlug, rate.RawDataJson))
            return false;

        return IsUnassignedCatalogSnapshot(
                rate.ContainerTypeName,
                rate.ContainerTypeCode,
                rate.ContainerTypeSlug)
            || IsUnassignedCatalogSnapshot(
                rate.CarrierName,
                rate.CarrierCode,
                rate.CarrierSlug);
    }

    private static bool HasExplicitFclEquipment(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var normalized = CanonicalText(value);
            if (normalized.Contains("fcl", StringComparison.Ordinal)) return true;

            var hasSize = normalized.Contains("20", StringComparison.Ordinal)
                || normalized.Contains("40", StringComparison.Ordinal)
                || normalized.Contains("45", StringComparison.Ordinal);
            var hasEquipmentType = normalized.Contains("highcube", StringComparison.Ordinal)
                || normalized.Contains("dryvan", StringComparison.Ordinal)
                || normalized.Contains("reefer", StringComparison.Ordinal)
                || normalized.Contains("opentop", StringComparison.Ordinal)
                || normalized.Contains("flatrack", StringComparison.Ordinal)
                || normalized.Contains("standard", StringComparison.Ordinal)
                || normalized.Contains("hc", StringComparison.Ordinal)
                || normalized.Contains("hq", StringComparison.Ordinal)
                || normalized.Contains("dv", StringComparison.Ordinal)
                || normalized.Contains("std", StringComparison.Ordinal)
                || normalized.Contains("rf", StringComparison.Ordinal)
                || normalized.Contains("ot", StringComparison.Ordinal)
                || normalized.Contains("fr", StringComparison.Ordinal);

            if (hasSize && hasEquipmentType) return true;
        }

        return false;
    }

    private static bool IsUnassignedCatalogSnapshot(params string?[] values)
    {
        var normalized = values
            .Select(CanonicalText)
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();

        if (normalized.Length == 0) return true;

        return normalized.Any(value => value is
            "porasignar" or "unassigned" or "pending" or "sinasignar"
            or "na" or "none" or "unknown" or "notapplicable" or "noaplica"
            or "sincontenedor" or "sincontainer" or "sinnaviera" or "sincarrier");
    }

    private static bool ContainsLclMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("lcl", StringComparison.Ordinal)
            || normalized.Contains("less than container load", StringComparison.Ordinal)
            || normalized.Contains("less-than-container-load", StringComparison.Ordinal)
            || normalized.Contains("coloader", StringComparison.Ordinal)
            || normalized.Contains("co-loader", StringComparison.Ordinal)
            || normalized.Contains("coloading", StringComparison.Ordinal)
            || normalized.Contains("groupage", StringComparison.Ordinal);
    }

    private static decimal ResolveImportedLclTotalCost(ImportFclRates rate)
    {
        return Math.Max(
            0m,
            rate.TotalCost
                ?? (rate.OceanFreight ?? rate.Freight)
                    + (rate.OriginCharges ?? 0m)
                    + (rate.DestinationCharges ?? 0m)
                    + (rate.Surcharges ?? 0m));
    }

    private static ColoaderLine[] BuildImportedLclLines(
        ImportFclRates rate,
        decimal totalCost,
        decimal totalSale)
    {
        var lines = new List<ColoaderLine>();
        var freight = Math.Max(0m, rate.OceanFreight ?? rate.Freight);

        void AddLine(
            byte discriminator,
            string name,
            string detailType,
            string chargeBasis,
            decimal amount)
        {
            if (amount <= 0m) return;

            lines.Add(new ColoaderLine(
                rate.Id,
                SyntheticLineId(rate.Id, discriminator),
                null,
                name,
                detailType,
                "Fixed",
                chargeBasis,
                rate.CurrencyId,
                rate.CurrencyName,
                rate.CurrencyCode,
                amount,
                amount,
                1m,
                0m,
                "Fuente LCL importada y preaprobada.",
                false,
                0m));
        }

        AddLine(1, "Flete internacional LCL", "Freight", "PerChargeableCbm", freight);
        AddLine(2, "Cargos de origen", "OriginCharge", "PerShipment", Math.Max(0m, rate.OriginCharges ?? 0m));
        AddLine(3, "Cargos de destino", "DestinationCharge", "PerShipment", Math.Max(0m, rate.DestinationCharges ?? 0m));
        AddLine(4, "Recargos", "Other", "PerShipment", Math.Max(0m, rate.Surcharges ?? 0m));

        if (lines.Count == 0 && totalSale > 0m)
        {
            lines.Add(new ColoaderLine(
                rate.Id,
                SyntheticLineId(rate.Id, 5),
                null,
                "Flete internacional LCL",
                "Freight",
                "Fixed",
                "PerChargeableCbm",
                rate.CurrencyId,
                rate.CurrencyName,
                rate.CurrencyCode,
                totalCost,
                totalSale,
                1m,
                totalSale - totalCost,
                "Fuente LCL importada y preaprobada.",
                false,
                0m));
            return lines.ToArray();
        }

        // The import format stores one optional commercial total instead of a
        // sale amount per concept. Keep every extracted cost intact and place
        // the commercial difference on the freight line (or first line).
        var commercialDelta = totalSale - totalCost;
        if (lines.Count > 0 && commercialDelta != 0m)
        {
            var index = lines.FindIndex(line => line.CostDetailType == "Freight");
            if (index < 0) index = 0;

            var current = lines[index];
            var adjustedSale = Math.Max(0m, current.SaleAmount + commercialDelta);
            lines[index] = current with
            {
                SaleAmount = adjustedSale,
                UtilityAmount = adjustedSale - current.CostAmount,
            };
        }

        return lines.ToArray();
    }

    private static Guid SyntheticLineId(Guid sourceId, byte discriminator)
    {
        var bytes = sourceId.ToByteArray();
        bytes[^1] ^= discriminator;
        return new Guid(bytes);
    }

    private static bool IsUnassignedLocation(string? name, string? code, string? slug)
    {
        var values = new[] { CanonicalText(name), CanonicalText(code), CanonicalText(slug) }
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();

        return values.Length == 0
            || values.Any(value => value is "porasignar" or "unassigned" or "pending");
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
